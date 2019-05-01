using Gdk;
using GLib;
using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

public partial class MainWindow : Gtk.Window
{
    FileChooserDialog ImageLoader;
    FileChooserDialog ImageSaver;
    FileChooserDialog FolderChooser;
    FileChooserDialog ClassifierChooser;

    Pixbuf OriginalImage;

    OpenCV cv = new OpenCV();

    bool IsSelecting;
    bool IsDragging;

    int X0, Y0, X1, Y1;
    int prevX, prevY;

    bool editEnabled;

    bool ControlsActive;

    TextIter start, end;

    Mutex Rendering = new Mutex();

    public MainWindow() : base(Gtk.WindowType.Toplevel)
    {
        Build();

        InitializeComponents();

        InitializeDefaults();

        DisableControls();

        InitializeSelectionMode();

        InitializeSelected();

        EnableControls();

        Idle.Add(new IdleHandler(OnIdleUpdate));
    }

    protected void InitializeComponents()
    {
        ImageLoader = new FileChooserDialog(
            "Choose the Image to open",
            this,
            FileChooserAction.Open,
            "Open", ResponseType.Accept,
            "Cancel", ResponseType.Cancel
        );

        ImageLoader.AddFilter(AddFilter("Image files (png/jpg/jpeg/tif/tiff/bmp/gif/ico/xpm/icns/pgm)", "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp", "*.gif", "*.ico", "*.xpm", "*.icns", "*.pgm"));

        ImageSaver = new FileChooserDialog(
            "Save Processed Image",
            this,
            FileChooserAction.Save,
            "Save", ResponseType.Accept,
            "Cancel", ResponseType.Cancel
        );

        ImageSaver.AddFilter(AddFilter("png", "*.png"));

        FolderChooser = new FileChooserDialog(
            "Choose the folder where blob images will be saved",
            this,
            FileChooserAction.SelectFolder,
            "Save", ResponseType.Accept,
            "Cancel", ResponseType.Cancel
        );

        ClassifierChooser = new FileChooserDialog(
            "Choose the Classifier to open",
            this,
            FileChooserAction.Open,
            "Open", ResponseType.Accept,
            "Cancel", ResponseType.Cancel
        );

        ClassifierChooser.AddFilter(AddFilter("Classifiers (xml)", "*.xml"));

        Threshold.Value = Detect.DeformablePartsModelThreshold;
    }

    protected FileFilter AddFilter(string name, params string[] patterns)
    {
        var filter = new FileFilter() { Name = name };

        foreach (var pattern in patterns)
            filter.AddPattern(pattern);

        return filter;
    }

    protected void InitializeDefaults()
    {
        OriginalImage = new Pixbuf(Colorspace.Rgb, false, 8, imageBox.WidthRequest, imageBox.HeightRequest);
        OriginalImage.Fill(0);

        imageBox.Pixbuf = OriginalImage.Copy().ScaleSimple(imageBox.WidthRequest, imageBox.HeightRequest, InterpType.Bilinear);
    }

    void InitializeSelected()
    {
        DisableEditSignals();

        if (GtkSelection.Selected > 0)
        {
            SetupEditRegion(GtkSelection.Selected);
            SetupEditRegionLocations(GtkSelection.Selected);
            editRegionLayout.Show();
        }

        EnableEditSignals();
    }

    void InitializeSelectionMode()
    {
        if (GtkSelection.Selection.EllipseMode)
        {
            SelectMode.Active = false;
            SelectMode.Label = "Ellipse Mode";
        }
        else
        {
            SelectMode.Active = false;
            SelectMode.Label = "Box Mode";
        }

        HideEdit();
    }

    void DisableControls()
    {
        ControlsActive = false;
    }

    void EnableControls()
    {
        ControlsActive = true;
    }

    void Redraw(Gtk.Image background)
    {
        if (background == null)
            return;

        var dest = background.GdkWindow;
        var gc = new Gdk.GC(dest);

        dest.DrawPixbuf(gc, background.Pixbuf, 0, 0, 0, 0, background.WidthRequest, background.HeightRequest, RgbDither.None, 0, 0);

        if (IsSelecting)
            GtkSelection.Draw(gc, dest, X0, Y0, X1, Y1);
    }

    protected void RenderImage(Pixbuf pixbuf)
    {
        if (pixbuf == null)
            return;

        if (imageBox.Pixbuf != null)
            imageBox.Pixbuf.Dispose();

        imageBox.Pixbuf = pixbuf.Copy().ScaleSimple(imageBox.WidthRequest, imageBox.HeightRequest, InterpType.Bilinear);
    }

    protected void Move()
    {
        var dx = X1 - prevX;
        var dy = Y1 - prevY;

        prevX = X1;
        prevY = Y1;

        GtkSelection.Selection.Move(dx, dy, GtkSelection.Selected);

        SetupEditRegionLocations(GtkSelection.Selected);
    }

    protected void SetupEditRegion(int Region)
    {
        if (Region > 0)
        {
            GtkSelection.Selection.Size(Region, out int width, out int height);
            SetupEditRegionLocations(GtkSelection.Selected);

            widthScale.Value = width;
            heightScale.Value = height;
            widthScaleNumeric.Value = width;
            heightScaleNumeric.Value = height;

            GtkSelection.Selection.GetScore(Region, out double score);

            Score.Text = Convert.ToString(score);

            GtkSelection.Selection.GetClass(Region, out string className);

            ClassName.Text = className;
        }
    }

    protected void SetupEditRegionLocations(int Region)
    {
        if (Region > 0)
        {
            GtkSelection.Selection.Location(Region, out int x, out int y);

            dxScale.Value = x;
            dxScaleNumeric.Value = x;
            dyScale.Value = y;
            dyScaleNumeric.Value = y;
        }
    }

    protected void DisableEditSignals()
    {
        editEnabled = false;
    }

    protected void EnableEditSignals()
    {
        editEnabled = true;
    }

    protected void RenderImage()
    {
        if (OriginalImage != null)
        {
            using (Pixbuf pb = GtkSelection.Render(OriginalImage.ScaleSimple(imageBox.WidthRequest, imageBox.HeightRequest, InterpType.Bilinear), cv, new Color(255, 0, 0), GtkSelection.Selected, GtkSelection.SelectedColor, false, true))
            {
                Scale(pb, imageBox.Pixbuf);
            }
        }

        CollectGarbage();
    }

    void Scale(Pixbuf source, Pixbuf target)
    {
        if (source != null && target != null)
            source.Scale(target, 0, 0, target.Width, target.Height, 0, 0, 1.0, 1.0, InterpType.Bilinear);
    }

    protected void ScaleResize()
    {
        if (ControlsActive && GtkSelection.Selected > 0)
        {
            int width = Convert.ToInt32(widthScale.Value);
            int height = Convert.ToInt32(heightScale.Value);

            GtkSelection.Selection.ReSize(GtkSelection.Selected, width, height);

            widthScaleNumeric.Value = width;
            heightScaleNumeric.Value = height;
        }
    }

    protected void NumericResize()
    {
        if (ControlsActive && GtkSelection.Selected > 0)
        {
            int width = Convert.ToInt32(widthScaleNumeric.Value);
            int height = Convert.ToInt32(heightScaleNumeric.Value);

            GtkSelection.Selection.ReSize(GtkSelection.Selected, width, height);

            widthScale.Value = width;
            heightScale.Value = height;
        }
    }

    protected void ScaleMove()
    {
        if (ControlsActive && GtkSelection.Selected > 0)
        {
            GtkSelection.Selection.Location(GtkSelection.Selected, out int x, out int y);

            GtkSelection.Selection.Move(Convert.ToInt32(dxScale.Value) - x, Convert.ToInt32(dyScale.Value) - y, GtkSelection.Selected);

            SetupEditRegionLocations(GtkSelection.Selected);
        }
    }

    protected void NumericMove()
    {
        if (ControlsActive && GtkSelection.Selected > 0)
        {
            GtkSelection.Selection.Location(GtkSelection.Selected, out int x, out int y);

            GtkSelection.Selection.Move(Convert.ToInt32(dxScaleNumeric.Value) - x, Convert.ToInt32(dyScaleNumeric.Value) - y, GtkSelection.Selected);

            SetupEditRegionLocations(GtkSelection.Selected);
        }
    }

    void CollectGarbage()
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }

    protected void HideEdit()
    {
        editRegionLayout.Hide();
        GtkSelection.Selected = 0;
        DisableEditSignals();
    }

    protected void ClearRegions()
    {
        GtkSelection.Selection.Clear();
        HideEdit();
    }

    protected void LoadImage(FileChooserDialog dialog, string title, ref Pixbuf pixbuf)
    {
        dialog.Title = title;

        if (!string.IsNullOrEmpty(dialog.Filename))
        {
            var directory = System.IO.Path.GetDirectoryName(dialog.Filename);

            if (Directory.Exists(directory))
            {
                dialog.SetCurrentFolder(directory);
            }
        }

        if (dialog.Run() == Convert.ToInt32(ResponseType.Accept))
        {
            if (!string.IsNullOrEmpty(dialog.Filename))
            {
                var FileName = dialog.Filename;

                var temp = new Pixbuf(FileName);

                if (pixbuf != null && temp != null)
                {
                    pixbuf.Dispose();

                    pixbuf = new Pixbuf(Colorspace.Rgb, false, 8, temp.Width, temp.Height);

                    temp.Composite(pixbuf, 0, 0, temp.Width, temp.Height, 0, 0, 1, 1, InterpType.Nearest, 255);
                }

                if (temp != null)
                    temp.Dispose();

                if (pixbuf != null)
                    RenderImage(pixbuf);

                ClearRegions();
            }
        }

        dialog.Hide();
    }

    protected void UpdateModels()
    {
        ModelsView.Buffer.Clear();
        ModelsView.Buffer.Text = "";

        var Queue = Detect.DeformablePartsModels;

        if (Detect.DeformablePartsModels.Count > 0)
        {
            var line = 0;

            foreach (var item in Queue)
            {
                ModelsView.Buffer.Text += item;

                if (line < Queue.Count - 1)
                    ModelsView.Buffer.Text += "\n";

                line++;
            }
        }
    }

    protected void Quit()
    {
        if (imageBox.Pixbuf != null)
            imageBox.Pixbuf.Dispose();

        if (OriginalImage != null)
            OriginalImage.Dispose();

        if (ImageLoader != null)
            ImageLoader.Dispose();

        if (ImageSaver != null)
            ImageSaver.Dispose();

        if (FolderChooser != null)
            FolderChooser.Dispose();

        Application.Quit();
    }

    bool OnIdleUpdate()
    {
        Rendering.WaitOne();

        RenderImage();

        Redraw(imageBox);

        Rendering.ReleaseMutex();

        return true;
    }

    protected void OnDeleteEvent(object sender, DeleteEventArgs a)
    {
        Quit();

        a.RetVal = true;
    }

    protected void ScaleMoveEvent(object o, EventArgs e)
    {
        if (ControlsActive && editEnabled)
            ScaleMove();
    }

    protected void NumericMoveEvent(object o, EventArgs e)
    {
        if (ControlsActive && editEnabled)
            NumericMove();
    }

    protected void CloseEdit(object o, EventArgs e)
    {
        if (ControlsActive)
        {
            HideEdit();
        }
    }

    protected void ScaleResizeEvent(object o, EventArgs e)
    {
        if (ControlsActive && editEnabled)
            ScaleResize();
    }

    protected void NumericResizeEvent(object o, EventArgs e)
    {
        if (ControlsActive && editEnabled)
            NumericResize();
    }

    protected void OnImageEventBoxMotionNotifyEvent(object o, MotionNotifyEventArgs args)
    {
        if (!IsSelecting && !IsDragging) return;

        X1 = Convert.ToInt32(args.Event.X);
        Y1 = Convert.ToInt32(args.Event.Y);

        if (IsDragging)
            Move();
    }

    protected void OnImageEventBoxButtonPressEvent(object o, ButtonPressEventArgs args)
    {
        X0 = Convert.ToInt32(args.Event.X);
        Y0 = Convert.ToInt32(args.Event.Y);

        X1 = X0;
        Y1 = Y0;

        if (args.Event.Button == 3)
        {
            IsSelecting = false;
            IsDragging = false;

            HideEdit();

            GtkSelection.Selection.Update(X0, Y0);
        }
        else
        {
            if (args.Event.Button == 1)
            {
                GtkSelection.Selected = GtkSelection.Selection.Find(X0, Y0);

                if (GtkSelection.Selected > 0)
                {
                    IsDragging = true;

                    InitializeSelected();

                    prevX = X0;
                    prevY = Y0;
                }
                else
                {
                    HideEdit();

                    IsSelecting = true;
                }
            }
        }
    }

    protected void OnImageEventBoxButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
    {
        if (IsSelecting)
        {
            IsSelecting = false;

            GtkSelection.Selection.Add(X0, Y0, X1, Y1);
        }

        if (IsDragging)
        {
            IsDragging = false;
        }
    }

    protected void ToggleSelectMode(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            if (SelectMode.Active)
            {
                SelectMode.Label = "Box Mode";
                GtkSelection.Selection.EllipseMode = false;
            }
            else
            {
                SelectMode.Label = "Ellipse Mode";
                GtkSelection.Selection.EllipseMode = true;
            }

            HideEdit();
        }
    }

    protected void OnClearButtonClicked(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            ClearRegions();
        }
    }

    protected void OnLoadImageButtonClicked(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            LoadImage(ImageLoader, "Load Image", ref OriginalImage);
        }
    }

    protected void OnDetectButtonClicked(object sender, EventArgs e)
    {
        if (ControlsActive && OriginalImage != null)
        {
            Detect.DeformablePartsModel(
                cv,
                OriginalImage,
                GtkSelection.Selection,
                Convert.ToDouble(imageBox.WidthRequest) / OriginalImage.Width,
                Convert.ToDouble(imageBox.HeightRequest) / OriginalImage.Height
            );
        }
    }

    protected void OnSelectModelButtonClicked(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            ClassifierChooser.Title = "Select Deformable Parts Model";

            if (!string.IsNullOrEmpty(ClassifierChooser.Filename))
            {
                var directory = System.IO.Path.GetDirectoryName(ClassifierChooser.Filename);

                if (Directory.Exists(directory))
                {
                    ClassifierChooser.SetCurrentFolder(directory);
                }
            }

            if (ClassifierChooser.Run() == Convert.ToInt32(ResponseType.Accept))
            {
                if (!string.IsNullOrEmpty(ClassifierChooser.Filename))
                {
                    Detect.DeformablePartsModelFile = ClassifierChooser.Filename;
                }
            }

            ClassifierChooser.Hide();
        }
    }

    protected void OnThresholdValueChanged(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            Detect.DeformablePartsModelThreshold = Convert.ToDouble(Threshold.Value);
        }
    }

    protected void OnSaveImageButtonClicked(object sender, EventArgs e)
    {
        if (ControlsActive && GtkSelection.Selection.Count() > 0)
        {
            ImageSaver.Title = "Save Processed Image";

            if (!string.IsNullOrEmpty(ImageSaver.Filename))
            {
                var directory = System.IO.Path.GetDirectoryName(ImageSaver.Filename);

                if (Directory.Exists(directory))
                {
                    ImageSaver.SetCurrentFolder(directory);
                }
            }

            if (ImageSaver.Run() == (int)ResponseType.Accept)
            {
                if (!string.IsNullOrEmpty(ImageSaver.Filename))
                {
                    var FileName = ImageSaver.Filename;

                    if (!FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        FileName += ".png";

                    var ScaleX = Convert.ToDouble(OriginalImage.Width) / imageBox.WidthRequest;
                    var ScaleY = Convert.ToDouble(OriginalImage.Height) / imageBox.HeightRequest;

                    using (Pixbuf pb = GtkSelection.Render(OriginalImage, cv, new Color(255, 0, 0), GtkSelection.Selected, GtkSelection.SelectedColor, false, true, ScaleX, ScaleY))
                    {
                        if (imageBox.Pixbuf != null && pb != null)
                        {
                            pb.Save(FileName, "png");
                        }
                    }
                }
            }

            ImageSaver.Hide();
        }
    }

    protected void OnSaveObjectsButtonClicked(object sender, EventArgs e)
    {
        if (ControlsActive && GtkSelection.Selection.Count() > 0)
        {
            // Add most recent directory
            if (!string.IsNullOrEmpty(FolderChooser.CurrentFolder))
            {
                if (Directory.Exists(FolderChooser.CurrentFolder))
                {
                    FolderChooser.SetCurrentFolder(FolderChooser.CurrentFolder);
                }
            }
            else if (!string.IsNullOrEmpty(ImageLoader.Filename))
            {
                var directory = System.IO.Path.GetDirectoryName(ImageLoader.Filename);

                if (Directory.Exists(directory))
                {
                    FolderChooser.SetCurrentFolder(directory);
                }
            }

            if (FolderChooser.Run() == Convert.ToInt32(ResponseType.Accept))
            {
                var blobs = GtkSelection.Selection.BoundingBoxes();

                if (!string.IsNullOrEmpty(FolderChooser.CurrentFolder) && !string.IsNullOrEmpty(ImageLoader.Filename) && blobs.Count > 0)
                {
                    var basefile = System.IO.Path.GetFileNameWithoutExtension(ImageLoader.Filename);

                    var index = 1;

                    foreach (var rectangle in blobs)
                    {
                        var ScaleX = Convert.ToDouble(OriginalImage.Width) / imageBox.WidthRequest;
                        var ScaleY = Convert.ToDouble(OriginalImage.Height) / imageBox.HeightRequest;

                        var width = Convert.ToInt32(Math.Abs(rectangle.X1 - rectangle.X0) * ScaleX);
                        var height = Convert.ToInt32(Math.Abs(rectangle.Y1 - rectangle.Y0) * ScaleY);
                        var top = Convert.ToInt32(Math.Min(rectangle.Y0, rectangle.Y1) * ScaleY);
                        var left = Convert.ToInt32(Math.Min(rectangle.X0, rectangle.X1) * ScaleX);

                        var area = new Pixbuf(Colorspace.Rgb, false, 8, width, height);

                        if (OriginalImage != null)
                        {
                            OriginalImage.CopyArea(left, top, width, height, area, 0, 0);
                            area.Save(string.Format("{0}/{1}-blob-{2}.png", FolderChooser.CurrentFolder, basefile, index.ToString("D4")), "png");
                        }

                        area.Dispose();

                        index++;
                    }
                }
            }

            FolderChooser.Hide();
        }
    }

    protected void OnRemoveModelButtonActivated(object sender, EventArgs e)
    {
        ModelsView.Buffer.GetSelectionBounds(out start, out end);

        var filter = start.Line;

        var queue = new List<string>();

        if (start.Line >= 0 && start.Line < Detect.DeformablePartsModels.Count && end.Line >= 0 && end.Line < Detect.DeformablePartsModels.Count)
        {
            for (var i = 0; i < Detect.DeformablePartsModels.Count; i++)
            {
                if (i < start.Line || i > end.Line)
                    queue.Add(Detect.DeformablePartsModels[i]);
            }

            Detect.DeformablePartsModels.Clear();

            Detect.DeformablePartsModels.AddRange(queue);

            UpdateModels();
        }
    }

    protected void OnClearModelsButtonActivated(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            Detect.DeformablePartsModels.Clear();

            UpdateModels();
        }
    }

    protected void OnApplyModelsButtonActivated(object sender, EventArgs e)
    {
        if (ControlsActive && OriginalImage != null)
        {
            Detect.DetectDeformablePartsModels(
                cv,
                OriginalImage,
                GtkSelection.Selection,
                Convert.ToDouble(imageBox.WidthRequest) / OriginalImage.Width,
                Convert.ToDouble(imageBox.HeightRequest) / OriginalImage.Height
            );
        }
    }

    protected void OnAddModelButtonActivated(object sender, EventArgs e)
    {
        if (ControlsActive)
        {
            ClassifierChooser.Title = "Select Deformable Parts Model";

            if (!string.IsNullOrEmpty(ClassifierChooser.Filename))
            {
                var directory = System.IO.Path.GetDirectoryName(ClassifierChooser.Filename);

                if (Directory.Exists(directory))
                {
                    ClassifierChooser.SetCurrentFolder(directory);
                }
            }

            if (ClassifierChooser.Run() == Convert.ToInt32(ResponseType.Accept))
            {
                if (!string.IsNullOrEmpty(ClassifierChooser.Filename))
                {
                    Detect.DeformablePartsModels.Add(ClassifierChooser.Filename);

                    UpdateModels();
                }
            }

            ClassifierChooser.Hide();
        }
    }
}
