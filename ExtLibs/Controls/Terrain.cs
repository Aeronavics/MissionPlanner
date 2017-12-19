using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Drawing.Imaging;
using System.Collections;
using System.Threading;
using System.Diagnostics;

using System.Drawing.Drawing2D;
using log4net;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
//using OpenTK.Graphics;
using MissionPlanner.Utilities; // GE xml alt reader
using MissionPlanner;


// Control written by Michael Oborne 2011
// dual opengl and GDI+

namespace MissionPlanner.Controls
{
    public class Terrain : GLControl
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private object paintlock = new object();
        private object streamlock = new object();

        private DateTime textureResetDateTime = DateTime.Now;

        /// <summary>
        /// this is to reduce cpu usage
        /// </summary>
        public bool streamjpgenable = false;

        public bool HoldInvalidation = false;

        public bool autoScale { get; set; }

        public bool displaygroundheight { get; set; }

        public bool displaysonar { get; set; }

        public bool frontview { get; set; }

        public bool waypointview { get; set; }

        public bool displayseaheight { get; set; }

        public bool displayhomealt { get; set; }

        public int fixd { get; set; }

        private class character
        {
            public Bitmap bitmap;
            public int gltextureid;
            public int width;
            public int size;
        }

        private Dictionary<int, character> charDict = new Dictionary<int, character>();

        public int huddrawtime = 0;

        [DefaultValue(true)]
        public bool opengl { get; set; }

        [Browsable(false)]
        public bool npotSupported { get; private set; }

        public bool SixteenXNine = false;

        [System.ComponentModel.Browsable(true), DefaultValue(true)]
        public bool displayalt { get; set; }

        [System.ComponentModel.Browsable(true), DefaultValue(true)]
        public bool bgon { get; set; }

        [System.ComponentModel.Browsable(true), DefaultValue(true)]
        public bool hudon { get; set; }

        [System.ComponentModel.Browsable(true), DefaultValue(true)]
        public bool displayheading { get; set; }

        private static ImageCodecInfo ici = GetImageCodec("image/jpeg");
        private static EncoderParameters eps = new EncoderParameters(1);

        private bool started = false;

        public Terrain()
        {
            opengl =
                displaygroundheight=

                                displayalt =
                                        displayheading =
                                            bgon = hudon =  true;

            this.Name = "Terrain";

            eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
            // or whatever other quality value you want

            objBitmap.MakeTransparent();

            graphicsObject = this;
            graphicsObjectGDIP = Graphics.FromImage(objBitmap);
        }


        private float _heading = 0;
        private float _alt = 0;
        private float _targetalt = 0;
        private float _groundspeed = 0;
        private float _disttowp = 0;
        private float _groundcourse = 0;
        private float _xtrack_error = 0;
        private float _turnrate = 0;
        private float _pitch = 0;
        private int _wpno = 0;
        private double _lng = 0;
        private double _lat = 0;
        private double _homealt = 0;
        public float diff = 1;
        public float shift = 0;
        public float _targetheading = 0;
        public float _alt_target = 0;
        private string _mode = "Manual";

        private float scale_factor = 0;
        private float prev_groundspeed = 0;
        private float speedchange = 0;
        private float speed = 0;



        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float heading
        {
            get { return _heading; }
            set
            {
                if (_heading != value)
                {
                    _heading = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float targetheading
        {
            get { return _targetheading; }
            set
            {
                if (_targetheading != value)
                {
                    _targetheading = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float targetalt
        {
            get { return _targetalt; }
            set
            {
                if (_targetalt != value)
                {
                    _targetalt = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float alt_target
        {
            get { return _alt_target; }
            set
            {
                if (_alt_target != value)
                {
                    _alt_target = value;
                    this.Invalidate();
                }
            }
        }


        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float alt
        {
            get { return _alt; }
            set
            {
                if (_alt != value)
                {
                    _alt = value;
                    this.Invalidate();
                }
            }
        }


        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float pitch
        {
            get { return _pitch; }
            set
            {
                if (_pitch != value)
                {
                    _pitch = value;
                    this.Invalidate();
                }
            }
        }


        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float disttowp
        {
            get { return _disttowp; }
            set
            {
                if (_disttowp != value)
                {
                    _disttowp = value;
                    this.Invalidate();
                }
            }
        }


        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public int wpno
        {
            get { return _wpno; }
            set
            {
                if (_wpno != value)
                {
                    _wpno = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float groundcourse
        {
            get { return _groundcourse; }
            set
            {
                if (_groundcourse != value)
                {
                    _groundcourse = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float xtrack_error
        {
            get { return _xtrack_error; }
            set
            {
                if (_xtrack_error != value)
                {
                    _xtrack_error = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float turnrate
        {
            get { return _turnrate; }
            set
            {
                if (_turnrate != value)
                {
                    _turnrate = value;
                    this.Invalidate();
                }
            }
        }


        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public double lng
        {
            get { return _lng; }
            set
            {
                if (_lng != value)
                {
                    _lng = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public double lat
        {
            get { return _lat; }
            set
            {
                if (_lat != value)
                {
                    _lat = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public double Homealt
        {
            get { return _homealt; }
            set
            {
                if (_homealt != value)
                {
                    _homealt = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public float groundspeed
        {
            get { return _groundspeed; }
            set
            {
                if (_groundspeed != value)
                {
                    _groundspeed = value;
                    this.Invalidate();
                }
            }
        }

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public string mode
        {
            get { return _mode; }
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    this.Invalidate();
                }
            }
        }


        public struct Custom
        {
            //public Point Position;
            //public float FontSize;
            public string Header;

            public System.Reflection.PropertyInfo Item;

            public double GetValue
            {
                get
                {
                    if (Item.PropertyType == typeof(Single))
                    {
                        return (double)(float)Item.GetValue(src, null);
                    }
                    if (Item.PropertyType == typeof(Int32))
                    {
                        return (double)(int)Item.GetValue(src, null);
                    }
                    if (Item.PropertyType == typeof(double))
                    {
                        return (double)Item.GetValue(src, null);
                    }

                    throw new Exception("Bad data type");
                }
            }

            public static object src { get; set; }
        }

        public Hashtable CustomItems = new Hashtable();

        [System.ComponentModel.Browsable(true), System.ComponentModel.Category("Values")]
        public Color hudcolor
        {
            get { return this._whitePen.Color; }
            set
            {
                _hudcolor = value;
                this._whitePen = new Pen(value, 2);
            }
        }

        private Color _hudcolor = Color.White;
        private Pen _whitePen = new Pen(Color.White, 2);
        private readonly SolidBrush _whiteBrush = new SolidBrush(Color.White);
        private readonly SolidBrush _blackBrush = new SolidBrush(Color.Black);
        private readonly SolidBrush _grenBrush = new SolidBrush(Color.FromArgb(0x9b, 0xb8, 0x24));
        private readonly SolidBrush _blueBrush = new SolidBrush(Color.Blue);

        private static readonly SolidBrush SolidBrush = new SolidBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff));

        private static readonly SolidBrush SlightlyTransparentWhiteBrush =
            new SolidBrush(Color.FromArgb(220, 255, 255, 255));

        private static readonly SolidBrush AltGroundBrush = new SolidBrush(Color.FromArgb(100, Color.BurlyWood));

        private readonly object _bgimagelock = new object();

        public Image bgimage
        {
            set
            {
                lock (this._bgimagelock)
                {
                    try
                    {
                        _bgimage = (Image)value;
                    }
                    catch
                    {
                        _bgimage = null;
                    }
                    this.Invalidate();
                }
            }
            get { return _bgimage; }
        }

        public Image drone
        {
            set
            {
            
                    try
                    {
                        _drone = (Image)value;
                    }
                    catch
                    {
                        _drone = null;
                    }
                    this.Invalidate();
 
            }
            get { return _drone; }
        }

        private Image _bgimage;

        private Image _drone;

        // move these global as they rarely change - reduce GC
        private Font font = new Font(HUDT.Font, 10);

        public Bitmap objBitmap = new Bitmap(1024, 1024, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        private int count = 0;
        private Terrain graphicsObject;
        private Graphics graphicsObjectGDIP;


        private System.ComponentModel.ComponentResourceManager resources =
            new System.ComponentModel.ComponentResourceManager(typeof(HUD));



        public override void Refresh()
        {
            if (!ThisReallyVisible())
            {
                //  return;
            }

            //base.Refresh();
            using (Graphics gg = this.CreateGraphics())
            {
                OnPaint(new PaintEventArgs(gg, this.ClientRectangle));
            }
        }

        DateTime lastinvalidate = DateTime.MinValue;

        /// <summary>
        /// Override to prevent offscreen drawing the control - mono mac
        /// </summary>
        public new void Invalidate()
        {
            if (HoldInvalidation)
                return;

            if (!ThisReallyVisible())
            {
                //  return;
            }

            lastinvalidate = DateTime.Now;

            base.Invalidate();
        }

        /// <summary>
        /// this is to fix a mono off screen drawing issue
        /// </summary>
        /// <returns></returns>
        public bool ThisReallyVisible()
        {
            //Control ctl = Control.FromHandle(this.Handle);
            return this.Visible;
        }

        protected override void OnLoad(EventArgs e)
        {
            log.Info("OnLoad Start");

            if (opengl && !DesignMode)
            {
                try
                {

                    OpenTK.Graphics.GraphicsMode test = this.GraphicsMode;
                    // log.Info(test.ToString());
                    log.Info("Vendor: " + GL.GetString(StringName.Vendor));
                    log.Info("Version: " + GL.GetString(StringName.Version));
                    log.Info("Device: " + GL.GetString(StringName.Renderer));
                    //Console.WriteLine("Extensions: " + GL.GetString(StringName.Extensions));

                    int[] viewPort = new int[4];

                    log.Debug("GetInteger");
                    GL.GetInteger(GetPName.Viewport, viewPort);
                    log.Debug("MatrixMode");
                    GL.MatrixMode(MatrixMode.Projection);
                    log.Debug("LoadIdentity");
                    GL.LoadIdentity();
                    log.Debug("Ortho");
                    GL.Ortho(0, Width, Height, 0, -1, 1);
                    log.Debug("MatrixMode");
                    GL.MatrixMode(MatrixMode.Modelview);
                    log.Debug("LoadIdentity");
                    GL.LoadIdentity();

                    log.Debug("PushAttrib");
                    GL.PushAttrib(AttribMask.DepthBufferBit);
                    log.Debug("Disable");
                    GL.Disable(EnableCap.DepthTest);
                    log.Debug("BlendFunc");
                    GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                    log.Debug("Enable");
                    GL.Enable(EnableCap.Blend);

                    string versionString = GL.GetString(StringName.Version);
                    string majorString = versionString.Split(' ')[0];
                    var v = new Version(majorString);
                    npotSupported = v.Major >= 2;
                }
                catch (Exception ex)
                {
                    log.Error("HUD opengl onload 1 ", ex);
                }

                try
                {
                    log.Debug("Hint");
                    GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);

                    GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
                    GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);
                    GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);

                    GL.Hint(HintTarget.TextureCompressionHint, HintMode.Nicest);
                }
                catch (Exception ex)
                {
                    log.Error("HUD opengl onload 2 ", ex);
                }

                try
                {
                    log.Debug("Enable");
                    GL.Enable(EnableCap.LineSmooth);
                    GL.Enable(EnableCap.PointSmooth);
                    GL.Enable(EnableCap.PolygonSmooth);

                }
                catch (Exception ex)
                {
                    log.Error("HUD opengl onload 3 ", ex);
                }
            }

            log.Info("OnLoad Done");

            started = true;
        }



        bool inOnPaint = false;
        string otherthread = "";


        protected override void OnPaint(PaintEventArgs e)
        {
            //GL.Enable(EnableCap.AlphaTest)

            // Console.WriteLine("hud paint");

            // Console.WriteLine("hud ms " + (DateTime.Now.Millisecond));

            if (!started)
                return;

            if (this.DesignMode)
            {
                e.Graphics.Clear(this.BackColor);
                e.Graphics.Flush();
                opengl = false;
                doPaint(e);
                opengl = true;
                return;
            }

 

            // force texture reset
            if (textureResetDateTime.Hour != DateTime.Now.Hour)
            {
                textureResetDateTime = DateTime.Now;
                doResize();
            }

            lock (this)
            {

                if (inOnPaint)
                {
                    log.Info("Was in onpaint Hud th:" + System.Threading.Thread.CurrentThread.Name + " in " +
                             otherthread);
                    return;
                }

                otherthread = System.Threading.Thread.CurrentThread.Name;

                inOnPaint = true;

            }

            try
            {

                if (opengl)
                {
                    // make this gl window and thread current
                    if (!Context.IsCurrent || DateTime.Now.Second % 5 == 0)
                        MakeCurrent();

                    GL.Clear(ClearBufferMask.ColorBufferBit);

                }

                doPaint(e);

                if (opengl)
                {
                    this.SwapBuffers();

                    // free from this thread
                    //Context.MakeCurrent(null);
                }

            }
            catch (Exception ex)
            {
                log.Info(ex.ToString());
            }

            count++;


            lock (this)
            {
                inOnPaint = false;
            }
        }

        void Clear(Color color)
        {
            if (opengl)
            {
                GL.ClearColor(color);

            }
            else
            {
                graphicsObjectGDIP.Clear(color);
            }
        }

        const double rad2deg = (180 / Math.PI);
        const double deg2rad = (1.0 / rad2deg);

        public void DrawArc(Pen penn, RectangleF rect, float start, float degrees)
        {
            if (opengl)
            {
                GL.LineWidth(penn.Width);
                GL.Color4(penn.Color);

                GL.Begin(PrimitiveType.LineStrip);

                start = 360 - start;
                start -= 30;

                float x = 0, y = 0;
                for (float i = start; i <= start + degrees; i++)
                {
                    x = (float)Math.Sin(i * deg2rad) * rect.Width / 2;
                    y = (float)Math.Cos(i * deg2rad) * rect.Height / 2;
                    x = x + rect.X + rect.Width / 2;
                    y = y + rect.Y + rect.Height / 2;
                    GL.Vertex2(x, y);
                }
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.DrawArc(penn, rect, start, degrees);
            }
        }

        public void FillPie(Brush brushh, Rectangle rect, float start, float degrees)
        {
            if (opengl)
            {
                GL.Begin(PrimitiveType.TriangleFan);
                GL.Color4(((SolidBrush)brushh).Color);

                start = 360 - start;
                start -= 30;

                float x = 0, y = 0;
                for (float i = start; i <= start + degrees; i++)
                {
                    x = (float)Math.Sin(i * deg2rad) * rect.Width / 2;
                    y = (float)Math.Cos(i * deg2rad) * rect.Height / 2;
                    x = x + rect.X + rect.Width / 2;
                    y = y + rect.Y + rect.Height / 2;
                    GL.Vertex2(x, y);
                }
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.FillPie(brushh, rect, start, degrees);
            }
        }

        public void DrawEllipse(Pen penn, Rectangle rect)
        {
            if (opengl)
            {
                GL.LineWidth(penn.Width);
                GL.Color4(penn.Color);

                GL.Begin(PrimitiveType.LineLoop);
                float x, y;
                for (float i = 0; i < 360; i += 1)
                {
                    x = (float)Math.Sin(i * deg2rad) * rect.Width / 2;
                    y = (float)Math.Cos(i * deg2rad) * rect.Height / 2;
                    x = x + rect.X + rect.Width / 2;
                    y = y + rect.Y + rect.Height / 2;
                    GL.Vertex2(x, y);
                }
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.DrawEllipse(penn, rect);
            }
        }

        public static Bitmap ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = SmoothingMode.HighSpeed;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.ClearOutputChannelColorProfile();
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        private character[] _texture = new character[2];

        public void DrawImage(Image img, float x, float y, float width, float height, int textureno = 0)
        {
            if (opengl)
            {
                if (img == null)
                    return;

                if (_texture[textureno] == null)
                    _texture[textureno] = new character();

                // If the image is already a bitmap and we support NPOT textures then simply use it.
                if (npotSupported && img is Bitmap)
                {
                    _texture[textureno].bitmap = (Bitmap)img;
                }
                else
                {
                    // Otherwise we have to resize img to be POT.
                    _texture[textureno].bitmap = ResizeImage(img, 512, 512);
                }

                // generate the texture
                if (_texture[textureno].gltextureid == 0)
                {
                    GL.GenTextures(1, out _texture[textureno].gltextureid);
                }

                GL.BindTexture(TextureTarget.Texture2D, _texture[textureno].gltextureid);

                BitmapData data = _texture[textureno].bitmap.LockBits(
                    new Rectangle(0, 0, _texture[textureno].bitmap.Width, _texture[textureno].bitmap.Height),
                    ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                // create the texture type/dimensions
                if (_texture[textureno].width != _texture[textureno].bitmap.Width)
                {
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, data.Width, data.Height, 0,
                        OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);

                    _texture[textureno].width = data.Width;
                }
                else
                {
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, data.Width, data.Height,
                        OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                }

                _texture[textureno].bitmap.UnlockBits(data);

                bool polySmoothEnabled = GL.IsEnabled(EnableCap.PolygonSmooth);
                if (polySmoothEnabled)
                    GL.Disable(EnableCap.PolygonSmooth);

                GL.Enable(EnableCap.Texture2D);

                GL.BindTexture(TextureTarget.Texture2D, _texture[textureno].gltextureid);

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                    (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                    (int)TextureWrapMode.ClampToEdge);

                GL.Begin(PrimitiveType.TriangleStrip);

                GL.TexCoord2(0.0f, 0.0f);
                GL.Vertex2(x, y);
                GL.TexCoord2(0.0f, 1.0f);
                GL.Vertex2(x, y + height);
                GL.TexCoord2(1.0f, 0.0f);
                GL.Vertex2(x + width, y);
                GL.TexCoord2(1.0f, 1.0f);
                GL.Vertex2(x + width, y + height);

                GL.End();

                GL.Disable(EnableCap.Texture2D);

                if (polySmoothEnabled)
                    GL.Enable(EnableCap.PolygonSmooth);
            }
            else
            {
                graphicsObjectGDIP.DrawImage(img, x, y, width, height);
            }
        }

        public void DrawPath(Pen penn, GraphicsPath gp)
        {
            try
            {
                DrawPolygon(penn, gp.PathPoints);
            }
            catch
            {
            }
        }

        public void FillPath(Brush brushh, GraphicsPath gp)
        {
            try
            {
                FillPolygon(brushh, gp.PathPoints);
            }
            catch
            {
            }
        }


        public void ResetTransform()
        {
            if (opengl)
            {
                GL.LoadIdentity();
            }
            else
            {
                graphicsObjectGDIP.ResetTransform();
            }
        }

        public void RotateTransform(float angle)
        {
            if (opengl)
            {
                GL.Rotate(angle, 0, 0, 1);
            }
            else
            {
                graphicsObjectGDIP.RotateTransform(angle);
            }
        }

        public void TranslateTransform(float x, float y)
        {
            if (opengl)
            {
                GL.Translate(x, y, 0f);
            }
            else
            {
                graphicsObjectGDIP.TranslateTransform(x, y);
            }
        }
        /*
        public void FillPolygon(Brush brushh, Point[] list)
        {
            if (opengl)
            {
                GL.Begin(PrimitiveType.TriangleFan);
                GL.Color4(((SolidBrush)brushh).Color);
                foreach (Point pnt in list)
                {
                    GL.Vertex2(pnt.X, pnt.Y);
                }
                GL.Vertex2(list[list.Length - 1].X, list[list.Length - 1].Y);
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.FillPolygon(brushh, list);
            }
        }
        */
        public void FillPolygon(Brush brushh, PointF[] list)
        {
            if (opengl)
            {
                GL.Begin(PrimitiveType.TriangleFan); //Quads
                GL.Color4(((SolidBrush)brushh).Color);
                foreach (PointF pnt in list)
                {
                    GL.Vertex2(pnt.X, pnt.Y);
                }
                //GL.Vertex2(list[0].X, list[0].Y);
                GL.Vertex2(list[list.Length - 1].X, list[list.Length - 1].Y);
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.FillPolygon(brushh, list);
            }
        }
        /*
        public void DrawPolygon(Pen penn, Point[] list)
        {
            if (opengl)
            {
                GL.LineWidth(penn.Width);
                GL.Color4(penn.Color);

                GL.Begin(PrimitiveType.LineLoop);
                foreach (Point pnt in list)
                {
                    GL.Vertex2(pnt.X, pnt.Y);
                }
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.DrawPolygon(penn, list);
            }
        }
        */
        public void DrawPolygon(Pen penn, PointF[] list)
        {
            if (opengl)
            {
                GL.LineWidth(penn.Width);
                GL.Color4(penn.Color);

                GL.Begin(PrimitiveType.LineLoop);
                foreach (PointF pnt in list)
                {
                    GL.Vertex2(pnt.X, pnt.Y);
                }

                GL.End();
            }
            else
            {
                graphicsObjectGDIP.DrawPolygon(penn, list);
            }
        }


        public void FillRectangle(Brush brushh, RectangleF rectf)
        {
            if (opengl)
            {
                float x1 = rectf.X;
                float y1 = rectf.Y;

                float width = rectf.Width;
                float height = rectf.Height;

                GL.Begin(PrimitiveType.Quads);

                GL.LineWidth(0);

                if (((Type)brushh.GetType()) == typeof(LinearGradientBrush))
                {
                    LinearGradientBrush temp = (LinearGradientBrush)brushh;
                    GL.Color4(temp.LinearColors[0]);
                }
                else
                {
                    GL.Color4(((SolidBrush)brushh).Color.R / 255f, ((SolidBrush)brushh).Color.G / 255f,
                        ((SolidBrush)brushh).Color.B / 255f, ((SolidBrush)brushh).Color.A / 255f);
                }

                GL.Vertex2(x1, y1);
                GL.Vertex2(x1 + width, y1);

                if (((Type)brushh.GetType()) == typeof(LinearGradientBrush))
                {
                    LinearGradientBrush temp = (LinearGradientBrush)brushh;
                    GL.Color4(temp.LinearColors[1]);
                }
                else
                {
                    GL.Color4(((SolidBrush)brushh).Color.R / 255f, ((SolidBrush)brushh).Color.G / 255f,
                        ((SolidBrush)brushh).Color.B / 255f, ((SolidBrush)brushh).Color.A / 255f);
                }

                GL.Vertex2(x1 + width, y1 + height);
                GL.Vertex2(x1, y1 + height);
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.FillRectangle(brushh, rectf);
            }
        }

        public void DrawRectangle(Pen penn, RectangleF rect)
        {
            DrawRectangle(penn, rect.X, rect.Y, rect.Width, rect.Height);
        }

        public void DrawRectangle(Pen penn, double x1, double y1, double width, double height)
        {

            if (opengl)
            {
                GL.LineWidth(penn.Width);
                GL.Color4(penn.Color);

                GL.Begin(PrimitiveType.LineLoop);
                GL.Vertex2(x1, y1);
                GL.Vertex2(x1 + width, y1);
                GL.Vertex2(x1 + width, y1 + height);
                GL.Vertex2(x1, y1 + height);
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.DrawRectangle(penn, (float)x1, (float)y1, (float)width, (float)height);
            }
        }

        public void DrawLine(Pen penn, double x1, double y1, double x2, double y2)
        {

            if (opengl)
            {
                GL.Color4(penn.Color);
                GL.LineWidth(penn.Width);

                GL.Begin(PrimitiveType.Lines);
                GL.Vertex2(x1, y1);
                GL.Vertex2(x2, y2);
                GL.End();
            }
            else
            {
                graphicsObjectGDIP.DrawLine(penn, (float)x1, (float)y1, (float)x2, (float)y2);
            }
        }

        private readonly Pen _blackPen = new Pen(Color.Black, 2);
        private readonly Pen _greenPen = new Pen(Color.Green, 2);
        private readonly Pen _redPen = new Pen(Color.Red, 2);
        private readonly Pen _grenPen = new Pen(Color.FromArgb(0x9b, 0xb8, 0x24));

        void doPaint(PaintEventArgs e)
        {
            //Console.WriteLine("hud paint "+DateTime.Now.Millisecond);
            bool isNaN = false;
            try
            {
                if (graphicsObjectGDIP == null || !opengl &&
                    (objBitmap.Width != this.Width || objBitmap.Height != this.Height))
                {
                    objBitmap = new Bitmap(this.Width, this.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    objBitmap.MakeTransparent();
                    graphicsObjectGDIP = Graphics.FromImage(objBitmap);

                    graphicsObjectGDIP.SmoothingMode = SmoothingMode.HighSpeed;
                    graphicsObjectGDIP.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphicsObjectGDIP.CompositingMode = CompositingMode.SourceOver;
                    graphicsObjectGDIP.CompositingQuality = CompositingQuality.HighSpeed;
                    graphicsObjectGDIP.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    graphicsObjectGDIP.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
                }

                graphicsObjectGDIP.InterpolationMode = InterpolationMode.Bilinear;

                try
                {
                    graphicsObject.Clear(Color.Transparent);
                }
                catch
                {
                    // this is the first posible opengl call
                    // in vmware fusion on mac, this fails, so switch back to legacy
                    opengl = false;
                }

                bgon = true;

                // localize it

                if (float.IsNaN(_heading))
                {
                    isNaN = true;
                    _heading = 0;
                    _alt = 0;
                }

                graphicsObject.TranslateTransform(this.Width / 2, this.Height / 2);

                graphicsObject.TranslateTransform(0, _alt);

                int fontsize = this.Height / 30; // = 10
                int fontoffset = fontsize - 10;


                int halfwidth = this.Width / 2;
                int halfheight = this.Height / 2;

                this._whiteBrush.Color = this._whitePen.Color;
                this._blackBrush.Color = this._blackPen.Color;

                // Reset pens
                this._blackPen.Width = 2;
                this._greenPen.Width = 2;
                this._redPen.Width = 2;

                this._whitePen.Color = _hudcolor;

                //Scale factor


                speedchange = _groundspeed - prev_groundspeed;

                if (speedchange < 0.5 && speedchange > -0.5)
                {
                    speedchange = 0;
                }

                speed += speedchange;

                prev_groundspeed = speed;

                if (autoScale)
                {
                    scale_factor = 1/(1 + speed/2);
                }

                else
                {
                    scale_factor = 50 / (float)fixd;
                }

                List<PointF> points = new List<PointF>();
                float angle = 0;
                
                if (frontview)
                {
                    angle = _heading;
                }

                else
                {
                    if(_targetheading<0)
                    {
                        _targetheading += 360;
                    }
                    angle = _targetheading;
                }

                TerrainElevation t = new TerrainElevation(points, angle, _lat, _lng, this.Width, this.Height, speed, autoScale, fixd,_homealt);
                diff = t.setdiff();
                shift = t.setshift();

                float drone_width = _drone.Width / 4 * scale_factor;
                float drone_height = _drone.Height / 4 * scale_factor;

                float xPos = this.Width / 2 - drone_width / 2; //  (points[2].X+points[3].X)/2
                float yPos = this.Height - (float)_homealt * diff - drone_height+shift;

                float ydrawing = this.Height - (float)_homealt * diff - (float)_alt * diff+shift;
                float yhome = this.Height - (float)_homealt * diff + shift;

                // draw sky
                if (bgon == true)
                {
                    RectangleF bg = new RectangleF(-halfwidth * 2, -halfheight * 2, this.Width * 2,
                        this.Height * 2);

                    if (bg.Height != 0)
                    {
                        using (LinearGradientBrush linearBrush = new LinearGradientBrush(
                            bg, Color.Blue, Color.LightBlue, LinearGradientMode.Vertical))
                        {
                            graphicsObject.FillRectangle(linearBrush, bg);
                        }
                    }

                    // draw ground
 
                    if (_lat != 0 && _lng != 0)
                    {


                        graphicsObject.ResetTransform();
                        //graphicsObject.TranslateTransform(0, _alt);
                        if (points.Count != 0)
                        {
                            var prev = points[0];
                            int i = 1;
                            PointF[] terrain_points = new PointF[points.Count+3];
                            terrain_points[0] = new PointF(0, this.Height);
                            
                            foreach (PointF p in points)
                            {
                                //graphicsObject.DrawLine(this._blackPen, p.X, this.Height, p.X, 0);
                                prev = p;
                                terrain_points[i] = p;
                                i++;
                            }
                            terrain_points[i] = new PointF(this.Width, points[points.Count-1].Y);
                            terrain_points[i+1] = new PointF(this.Width, this.Height);

                            if (_alt * diff > halfheight)
                            {
                                graphicsObject.ResetTransform();
                                //graphicsObject.TranslateTransform(0, this.Height-_alt * diff);

                                graphicsObject.DrawPolygon(this._grenPen, terrain_points);
                                graphicsObject.FillPolygon(this._grenBrush, terrain_points);
                                graphicsObject.ResetTransform();
                            }

                            else
                            {
                                graphicsObject.DrawPolygon(this._grenPen, terrain_points);
                                graphicsObject.FillPolygon(this._grenBrush, terrain_points);
                            }
                        }

                        //Draw Drone
                       

                        if (this.Height-yPos + _alt * diff < 3*this.Height/4)
                        {
                            graphicsObject.ResetTransform();
                            graphicsObject.TranslateTransform(0, -_alt * diff);
                        }

                        else
                        {
                            graphicsObject.ResetTransform();
                            //graphicsObject.TranslateTransform(0, this.Height/4-yPos);
                            graphicsObject.TranslateTransform(0, -_alt * diff);
                        }

                        float movex = (_drone.Width / 4) / 2f + xPos;
                        float movey = (_drone.Height / 4) / 2f + yPos;

                        graphicsObject.TranslateTransform(movex, movey);
                        graphicsObject.RotateTransform(-_pitch);        //Rotate drone according to pitch
                        graphicsObject.TranslateTransform(-movex, -movey);

                        graphicsObject.DrawImage(_drone, xPos, yPos, drone_width, drone_height, 1);


                        graphicsObject.ResetTransform();
                        if (_mode == "Auto")
                        {
                            graphicsObject.DrawLine(this._blackPen, this.Width - 30, this.Height - (_homealt + _alt_target) * diff+shift, this.Width, this.Height - (_homealt + _alt_target) * diff+shift);
                        }

                        else if (_mode == "Guided")
                        {
                            graphicsObject.DrawLine(this._blackPen, this.Width - 30, this.Height - (_homealt + _alt_target) * diff+shift, this.Width, this.Height - (_homealt + _alt_target) * diff+shift);
                        }
                        
                    }

                    else
                    {
                        bg = new RectangleF(-halfwidth * 2, 0, this.Width * 2, halfheight * 2);

                        using (LinearGradientBrush linearBrush = new LinearGradientBrush(
                                    bg, Color.FromArgb(0x9b, 0xb8, 0x24), Color.FromArgb(0x41, 0x4f, 0x07),
                                    LinearGradientMode.Vertical))
                        {
                            graphicsObject.FillRectangle(linearBrush, bg);
                        }
                        //Draw Drone
                        graphicsObject.ResetTransform();
                        Rectangle drone = new Rectangle(this.Width / 4, this.Height/2- halfwidth / 16, halfwidth / 16, halfwidth / 16);
                        //graphicsObject.DrawEllipse(this._whitePen, drone);
                        graphicsObject.DrawImage(_drone, this.Width / 5 - _drone.Width/8, this.Height / 2 - _drone.Height / 4, _drone.Width / 4, _drone.Height / 4, 1);
                    }



                }
             
                graphicsObject.ResetTransform();
                // Left scroller
                Rectangle scrollbg = new Rectangle(0, 0, this.Width / 10, this.Height);

                if (displayseaheight)
                {
                    //graphicsObject.DrawRectangle(this._whitePen, scrollbg);

                    //graphicsObject.FillRectangle(SolidBrush, scrollbg);

                    PointF[] arrow = new PointF[5];

                    arrow[0] = new PointF(0, -10);
                    arrow[1] = new PointF(scrollbg.Width - 10, -10);
                    arrow[2] = new PointF(scrollbg.Width - 5, 0);
                    arrow[3] = new PointF(scrollbg.Width - 10, 10);
                    arrow[4] = new PointF(0, 10);

                    graphicsObject.TranslateTransform(0, this.Height);

                    int viewrange = 26;

                    float space = (scrollbg.Height) / (float)viewrange;
                    long start = ((int)_alt - viewrange / 2);
       
                    this._greenPen.Width = 4;


                    // draw arrow and text
                    graphicsObject.ResetTransform();
                    graphicsObject.TranslateTransform(0, ydrawing);
                    graphicsObject.DrawPolygon(this._blackPen, arrow);
                    graphicsObject.FillPolygon(Brushes.Black, arrow);
                    
                    drawstring(graphicsObject, ((int)_alt+_homealt).ToString("0 m"), font, 10, (SolidBrush)Brushes.AliceBlue,
                      scrollbg.Right - 45, -9);
                    graphicsObject.ResetTransform();

                    this._blackPen.Width = 6;
                    graphicsObject.DrawLine(this._blackPen, 3, this.Height-10, 3, ydrawing + 5);

                    //Draw Sea
                    graphicsObject.ResetTransform();
                    Rectangle sea = new Rectangle(0, this.Height - 10, this.Width, 10);
                    graphicsObject.FillRectangle(this._blueBrush, sea);

                }
            
                //draw heading ind
                Rectangle headbg = new Rectangle(0, 0, this.Width - 0, this.Height / 14);

                graphicsObject.ResetTransform();

                if (displayheading)
                {

                    // center
                    //   graphicsObject.DrawLine(redPen, headbg.Width / 2, headbg.Bottom, headbg.Width / 2, headbg.Top);

                    //bottom line
                    graphicsObject.DrawLine(this._whitePen, headbg.Left + 5, headbg.Bottom - 5, headbg.Width - 5,
                        headbg.Bottom - 5);

                    float total_dist = 0;
                    int minus = 0;

                    if (autoScale)
                    {
                        total_dist = 100 + 20 * (int)speed;
                        minus = (int)total_dist/2;
                    }

                    else
                    {
                        total_dist = fixd * 2;
                        minus = fixd;
                    }

                    float space = (headbg.Width-10) / (total_dist/10);
                    float spacetot = 0;
                    for (int a = 0; a <= total_dist; a +=(10))
                    {

                        graphicsObject.DrawLine(this._whitePen, headbg.Left + 5 + spacetot ,
                              headbg.Bottom - 5, headbg.Left + 5 + spacetot, headbg.Bottom - 10);
                        int b = 0;
                        if (minus < 100)
                        {
                            b = 2;
                        }

                        else
                        {
                            b = 5;
                        }

                        if (a / 10 % b == 0)
                        {
                            if (a - minus < 0)
                            { 
                                drawstring(graphicsObject, String.Format("{0,3}", a - minus), font, fontsize*scale_factor,
                                    _whiteBrush, headbg.Left + spacetot,
                                    headbg.Bottom - 24 - (int)(fontoffset * 1.7));
                            }

                            else
                            {
                                drawstring(graphicsObject, String.Format("{0,3}", a - minus), font, fontsize*scale_factor,
                                    _whiteBrush, headbg.Left + spacetot-12,
                                    headbg.Bottom - 24 - (int)(fontoffset * 1.7));
                            }
                        }

                        spacetot += space;

                    }

                }

                //Height above ground scale
                if (displaygroundheight)
                {

                    graphicsObject.ResetTransform();
                    graphicsObject.DrawLine(this._whitePen, xPos-8, points[points.Count / 2].Y, xPos-8, ydrawing);

                    graphicsObject.DrawLine(this._whitePen, xPos-16, ydrawing, xPos, ydrawing);

                    drawstring(graphicsObject, ((int)_alt + _homealt - (this.Height + shift - points[(points.Count) / 2].Y) / diff).ToString("0 m"), font, 10, (SolidBrush)Brushes.AliceBlue,
                            this.Width / 5 + 36, (ydrawing+points[(points.Count)/2].Y)/2);
                }

                //Home Altutide
                if (displayhomealt)
                {
                    // Set the SmoothingMode property to smooth the line.
                    e.Graphics.SmoothingMode =
                        System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Create a new Pen object.
                    Pen whitPen = new Pen(Color.White);

                    // Set the width to 6.
                    whitPen.Width = 6.0F;

                    // Set the DashCap to round.
                    whitPen.DashCap = System.Drawing.Drawing2D.DashCap.Round;

                    // Create a custom dash pattern.
                    whitPen.DashPattern = new float[] { 3, 2, 2, 3 };

                    float[] dashValues = { 15, 15, 15, 15 };
                    Pen blackPen = new Pen(Color.Black, 2);
                    blackPen.DashStyle = DashStyle.Dash;


                    // Draw a line.
                    graphicsObject.DrawLine(blackPen, 0, yhome, this.Width, yhome);
                    drawstring(graphicsObject, String.Format("Home Alt"), font, fontsize * scale_factor,
                        _whiteBrush, halfwidth, yhome + 5 + (int)(fontoffset * 1.7));

                    graphicsObject.DrawLine(blackPen, xPos + drone_width+8, yhome, xPos + drone_width+8, ydrawing);
                    graphicsObject.DrawLine(blackPen, xPos + drone_width + 16, ydrawing, xPos + drone_width, ydrawing);

                    drawstring(graphicsObject, ((int)_alt).ToString("0 m"), font, 10, (SolidBrush)Brushes.AliceBlue, xPos + drone_width + 16, yhome - (float)_alt*diff/2);
                }

                if (displaysonar)
                {
                    graphicsObject.DrawLine(_redPen, xPos + drone_width / 2, ydrawing, xPos + drone_width / 2 , yPos + 25);
                }


                if (isNaN)
                    drawstring(graphicsObject, "NaN Error " + DateTime.Now, font, this.Height / 30 + 10,
                        (SolidBrush)Brushes.Red, 50, 50);

                graphicsObject.ResetTransform();

                if (!opengl)
                {
                    e.Graphics.DrawImageUnscaled(objBitmap, 0, 0);
                }

                if (DesignMode)
                {
                    return;
                }

            }
            catch (Exception ex)
            {
                log.Info("hud2 error " + ex.ToString());
            }
        }

        static ImageCodecInfo GetImageCodec(string mimetype)
        {
            foreach (ImageCodecInfo ici in ImageCodecInfo.GetImageEncoders())
            {
                if (ici.MimeType == mimetype) return ici;
            }
            return null;
        }

        // Returns a System.Drawing.Bitmap with the contents of the current framebuffer
        public new Bitmap GrabScreenshot()
        {
            if (OpenTK.Graphics.GraphicsContext.CurrentContext == null)
                throw new OpenTK.Graphics.GraphicsContextMissingException();

            Bitmap bmp = new Bitmap(this.ClientSize.Width, this.ClientSize.Height);
            System.Drawing.Imaging.BitmapData data =
                bmp.LockBits(this.ClientRectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            GL.ReadPixels(0, 0, this.ClientSize.Width, this.ClientSize.Height, OpenTK.Graphics.OpenGL.PixelFormat.Bgr,
                PixelType.UnsignedByte, data.Scan0);
            bmp.UnlockBits(data);

            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
            return bmp;
        }


        float wrap360(float noin)
        {
            if (noin < 0)
                return noin + 360;
            return noin;
        }

        /// <summary>
        /// pen for drawstring
        /// </summary>
        private readonly Pen _p = new Pen(Color.FromArgb(0x26, 0x27, 0x28), 2f);

        /// <summary>
        /// pth for drawstring
        /// </summary>
        private readonly GraphicsPath pth = new GraphicsPath();

        void drawstring(Terrain e, string text, Font font, float fontsize, SolidBrush brush, float x, float y)
        {
            if (!opengl)
            {
                drawstring(graphicsObjectGDIP, text, font, fontsize, brush, x, y);
                return;
            }

            if (text == null || text == "")
                return;
            /*
            OpenTK.Graphics.Begin(); 
            GL.PushMatrix(); 
            GL.Translate(x, y, 0);
            printer.Print(text, font, c); 
            GL.PopMatrix(); printer.End();
            */

            float maxy = 1;

            foreach (char cha in text)
            {
                int charno = (int)cha;

                int charid = charno ^ (int)(fontsize * 1000) ^ brush.Color.ToArgb();

                if (!charDict.ContainsKey(charid))
                {
                    charDict[charid] = new character()
                    {
                        bitmap = new Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb),
                        size = (int)fontsize
                    };

                    charDict[charid].bitmap.MakeTransparent(Color.Transparent);

                    //charbitmaptexid

                    float maxx = this.Width / 150; // for space


                    // create bitmap
                    using (Graphics gfx = Graphics.FromImage(charDict[charid].bitmap))
                    {
                        pth.Reset();

                        if (text != null)
                            pth.AddString(cha + "", font.FontFamily, 0, fontsize + 5, new Point((int)0, (int)0),
                                StringFormat.GenericTypographic);

                        gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                        gfx.DrawPath(this._p, pth);

                        //Draw the face

                        gfx.FillPath(brush, pth);


                        if (pth.PointCount > 0)
                        {
                            foreach (PointF pnt in pth.PathPoints)
                            {
                                if (pnt.X > maxx)
                                    maxx = pnt.X;

                                if (pnt.Y > maxy)
                                    maxy = pnt.Y;
                            }
                        }
                    }

                    charDict[charid].width = (int)(maxx + 2);


                    // create texture
                    int textureId;
                    GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode,
                        (float)TextureEnvModeCombine.Replace); //Important, or wrong color on some computers

                    Bitmap bitmap = charDict[charid].bitmap;
                    GL.GenTextures(1, out textureId);
                    GL.BindTexture(TextureTarget.Texture2D, textureId);

                    BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, data.Width, data.Height, 0,
                        OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);

                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                        (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                        (int)TextureMagFilter.Linear);

                    //    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Nearest);
                    //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Nearest);
                    GL.Flush();
                    bitmap.UnlockBits(data);

                    charDict[charid].gltextureid = textureId;
                }

                float scale = 1.0f;

                // dont draw spaces
                if (cha != ' ')
                {
                    //GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

                    GL.Enable(EnableCap.Texture2D);
                    GL.BindTexture(TextureTarget.Texture2D, charDict[charid].gltextureid);

                    GL.Begin(PrimitiveType.Quads);
                    GL.TexCoord2(0, 0);
                    GL.Vertex2(x, y);
                    GL.TexCoord2(1, 0);
                    GL.Vertex2(x + charDict[charid].bitmap.Width * scale, y);
                    GL.TexCoord2(1, 1);
                    GL.Vertex2(x + charDict[charid].bitmap.Width * scale, y + charDict[charid].bitmap.Height * scale);
                    GL.TexCoord2(0, 1);
                    GL.Vertex2(x + 0, y + charDict[charid].bitmap.Height * scale);
                    GL.End();

                    //GL.Disable(EnableCap.Blend);
                    GL.Disable(EnableCap.Texture2D);
                }
                x += charDict[charid].width * scale;
            }
        }

        void drawstring(Graphics e, string text, Font font, float fontsize, SolidBrush brush, float x, float y)
        {
            if (text == null || text == "")
                return;

            float maxy = 0;

            foreach (char cha in text)
            {
                int charno = (int)cha;

                int charid = charno ^ (int)(fontsize * 1000) ^ brush.Color.ToArgb();

                if (!charDict.ContainsKey(charid))
                {
                    charDict[charid] = new character()
                    {
                        bitmap = new Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb),
                        size = (int)fontsize
                    };

                    charDict[charid].bitmap.MakeTransparent(Color.Transparent);

                    //charbitmaptexid

                    float maxx = this.Width / 150; // for space


                    // create bitmap
                    using (Graphics gfx = Graphics.FromImage(charDict[charid].bitmap))
                    {
                        pth.Reset();

                        if (text != null)
                            pth.AddString(cha + "", font.FontFamily, 0, fontsize + 5, new Point((int)0, (int)0),
                                StringFormat.GenericTypographic);

                        gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                        gfx.DrawPath(this._p, pth);

                        //Draw the face

                        gfx.FillPath(brush, pth);


                        if (pth.PointCount > 0)
                        {
                            foreach (PointF pnt in pth.PathPoints)
                            {
                                if (pnt.X > maxx)
                                    maxx = pnt.X;

                                if (pnt.Y > maxy)
                                    maxy = pnt.Y;
                            }
                        }
                    }

                    charDict[charid].width = (int)(maxx + 2);
                }

                // draw it

                float scale = 1.0f;
                // dont draw spaces
                if (cha != ' ')
                {
                    DrawImage(charDict[charid].bitmap, (int)x, (int)y, charDict[charid].bitmap.Width,
                        charDict[charid].bitmap.Height, charDict[charid].gltextureid);
                }
                else
                {

                }

                x += charDict[charid].width * scale;
            }

        }

        protected override void OnHandleCreated(EventArgs e)
        {
            try
            {
                if (opengl && !DesignMode)
                {
                    base.OnHandleCreated(e);
                }
            }
            catch (Exception ex)
            {
                log.Error("Expected failure on max/linux due to opengl support");
                log.Error(ex);
                opengl = false;
            } // macs/linux fail here
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try
            {
                if (opengl && !DesignMode)
                {
                    base.OnHandleDestroyed(e);
                }
            }
            catch (Exception ex)
            {
                log.Info(ex.ToString());
                opengl = false;
            }
        }

        public void doResize()
        {
            OnResize(EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            if (DesignMode || !IsHandleCreated || !started)
                return;

            base.OnResize(e);

            if (SixteenXNine)
            {
                int ht = (int)(this.Width / 1.777f);
                if (ht >= this.Height + 5 || ht <= this.Height - 5)
                {
                    this.Height = ht;
                    return;
                }
            }
            else
            {
                /*
                // 4x3
                int ht = 500;// (int)(this.Width);// / 1.333f);
                if (ht >= this.Height + 5 || ht <= this.Height - 5)
                {
                    this.Height = ht;
                    return;
                }
                */
            }

            graphicsObjectGDIP = Graphics.FromImage(objBitmap);

            try
            {
                foreach (character texid in charDict.Values)
                {
                    try
                    {
                        texid.bitmap.Dispose();
                    }
                    catch
                    {
                    }
                }

                if (opengl)
                {
                    foreach (character texid in _texture)
                    {
                        if (texid != null && texid.gltextureid != 0)
                            GL.DeleteTexture(texid.gltextureid);
                    }
                    this._texture = new character[_texture.Length];

                    foreach (character texid in charDict.Values)
                    {
                        if (texid.gltextureid != 0)
                            GL.DeleteTexture(texid.gltextureid);
                    }
                }

                charDict.Clear();
            }
            catch
            {
            }

            try
            {
                if (opengl)
                {
                    MakeCurrent();

                    GL.MatrixMode(MatrixMode.Projection);
                    GL.LoadIdentity();
                    GL.Ortho(0, Width, Height, 0, -1, 1);
                    GL.MatrixMode(MatrixMode.Modelview);
                    GL.LoadIdentity();

                    GL.Viewport(0, 0, Width, Height);
                }
            }
            catch
            {
            }

            Refresh();
        }

        [Browsable(false)]
        public new bool VSync
        {
            get
            {
                try
                {
                    return base.VSync;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                try
                {
                    base.VSync = value;
                }
                catch
                {
                }
            }
        }
    }
}