using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using com.drew.imaging.jpg;
using com.drew.metadata;
using com.drew.metadata.exif;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using log4net;
using MissionPlanner.GCSViews;
using MissionPlanner.Maps;
using MissionPlanner.Properties;
using MissionPlanner.Utilities;
using MissionPlanner.ArduPilot;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace MissionPlanner
{
    public partial class FaceMapUI : Form
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // Variables
        const double rad2deg = (180 / Math.PI);
        const double deg2rad = (1.0 / rad2deg);

        private FaceMapPlugin plugin;
        static public Object thisLock = new Object();

        GMapOverlay routesOverlay;
        GMapOverlay kmlpolygonsoverlay;
        List<PointLatLngAlt> list = new List<PointLatLngAlt>();
        List<PointLatLngAlt> grid;
        bool loadedfromfile = false;
        bool loading = false;

        Dictionary<string, camerainfo> cameras = new Dictionary<string, camerainfo>();

        public string DistUnits = "";
        public string inchpixel = "";
        public string feet_fovH = "";
        public string feet_fovV = "";
        double viewwidth = 0;
        double viewheight = 0;

        decimal camVerticalSpacing = 0;

        internal PointLatLng MouseDownStart = new PointLatLng();
        internal PointLatLng MouseDownEnd;
        internal PointLatLngAlt CurrentGMapMarkerStartPos;
        PointLatLng currentMousePosition;
        GMapMarker marker;
        GMapMarker CurrentGMapMarker = null;
        GMapMarkerOverlapCount GMapMarkerOverlap = new GMapMarkerOverlapCount(PointLatLng.Empty);
        int CurrentGMapMarkerIndex = 0;
        bool isMouseDown = false;
        bool isMouseDraging = false;

        // Structures
        public struct camerainfo
        {
            public string name;
            public float focallen;
            public float sensorwidth;
            public float sensorheight;
            public float imagewidth;
            public float imageheight;
        }

        public struct FaceMapData
        {
            public List<PointLatLngAlt> poly;
            //simple
            public string camera;
            public decimal benchheight;
            public decimal angle;
            public bool facedirection;
            public decimal speed;
            public bool usespeed;
            public bool autotakeoff;
            public bool autotakeoff_RTL;
            public bool extraimages;

            public decimal splitmission;

            public decimal bermdepth;
            public decimal numbenches;
            public decimal camerapitch;
            public decimal toeheight;
            public bool campitchunlock;
            //options
            public decimal dist;
            public string startfrom;
            public decimal overlap;
            public decimal sidelap;
            public decimal spacing;
            public bool crossgrid;
            // Copter Settings
            public decimal copter_delay;

            // camera config
            public bool trigdist;
            public bool digicam;
            public bool repeatservo;

            public bool breaktrigdist;

            public decimal repeatservo_no;
            public decimal repeatservo_pwm;
            public decimal repeatservo_cycle;

            // do set servo
            public decimal setservo_no;
            public decimal setservo_low;
            public decimal setservo_high;
        }

        public FaceMapUI(FaceMapPlugin plugin)
        {
            this.plugin = plugin;

            InitializeComponent();

            loading = true;

            map.MapProvider = plugin.Host.FDMapType;

            kmlpolygonsoverlay = new GMapOverlay("kmlpolygons");
            map.Overlays.Add(kmlpolygonsoverlay);

            routesOverlay = new GMapOverlay("routes");
            map.Overlays.Add(routesOverlay);

            // Map Events
            map.OnMapZoomChanged += new MapZoomChanged(map_OnMapZoomChanged);
            map.OnMarkerEnter += new MarkerEnter(map_OnMarkerEnter);
            map.OnMarkerLeave += new MarkerLeave(map_OnMarkerLeave);
            map.MouseUp += new MouseEventHandler(map_MouseUp);

            map.OnRouteEnter += new RouteEnter(map_OnRouteEnter);
            map.OnRouteLeave += new RouteLeave(map_OnRouteLeave);

            var points = plugin.Host.FPDrawnPolygon;
            points.Points.ForEach(x => { list.Add(x); });
            points.Dispose();
            if (plugin.Host.config["distunits"] != null)
                DistUnits = plugin.Host.config["distunits"].ToString();

            if (plugin.Host.cs.firmware == Firmwares.ArduPlane)
                NUM_UpDownFlySpeed.Value = (decimal) (12*CurrentState.multiplierspeed);

            map.MapScaleInfoEnabled = true;
            map.ScalePen = new Pen(Color.Orange);

            foreach (var temp in FlightData.kmlpolygons.Polygons)
            {
                kmlpolygonsoverlay.Polygons.Add(new GMapPolygon(temp.Points, "") {Fill = Brushes.Transparent});
            }
            foreach (var temp in FlightData.kmlpolygons.Routes)
            {
                kmlpolygonsoverlay.Routes.Add(new GMapRoute(temp.Points,""));
            }

            xmlcamera(false, Settings.GetRunningDirectory() + "camerasBuiltin.xml");

            xmlcamera(false, Settings.GetUserDataDirectory() + "cameras.xml");

            loading = false;
        }

        private void FaceMapUI_Load(object sender, EventArgs e)
        {
            loading = true;
            if (!loadedfromfile)
                loadsettings();

            TRK_zoom.Value = (float)map.Zoom;
            loading = false;

            domainUpDown1_ValueChanged(this, null);
        }

        private void FaceMapUI_Resize(object sender, EventArgs e)
        {
            map.ZoomAndCenterMarkers("polygons");
        }

        // Load/Save
        public void LoadFaceMap()
        {
            System.Xml.Serialization.XmlSerializer reader = new System.Xml.Serialization.XmlSerializer(typeof(FaceMapData));

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "*.facemap|*.facemap";
                ofd.ShowDialog();

                if (File.Exists(ofd.FileName))
                {
                    using (StreamReader sr = new StreamReader(ofd.FileName))
                    {
                        var test = (FaceMapData)reader.Deserialize(sr);

                        loading = true;
                        loadfacemapdata(test);
                        loading = false;
                    }
                }
            }
        }

        public void SaveFaceMap()
        {
            System.Xml.Serialization.XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(FaceMapData));

            var facemapdata = saveFaceMapData();

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "*.facemap|*.facemap";
                sfd.ShowDialog();

                if (sfd.FileName != "")
                {
                    using (StreamWriter sw = new StreamWriter(sfd.FileName))
                    {
                        writer.Serialize(sw, facemapdata);
                    }
                }
            }
        }

        void loadfacemapdata(FaceMapData facemapdata)
        {
            list = facemapdata.poly;

            CMB_camera.Text = facemapdata.camera;
            NUM_BenchHeight.Value = facemapdata.benchheight;
            NUM_angle.Value = facemapdata.angle;
            CHK_facedirection.Checked = facemapdata.facedirection;
            CHK_usespeed.Checked = facemapdata.usespeed;
            NUM_UpDownFlySpeed.Value = facemapdata.speed;
            CHK_toandland.Checked = facemapdata.autotakeoff;
            CHK_toandland_RTL.Checked = facemapdata.autotakeoff_RTL;
            NUM_split.Value = facemapdata.splitmission;

            NUM_BermDepth.Value = facemapdata.bermdepth;
            NUM_Benches.Value = facemapdata.numbenches;
            NUM_cameraPitch.Value = facemapdata.camerapitch;
            NUM_toeHeight.Value = facemapdata.toeheight;
            CHK_camPitchUnlock.Checked = facemapdata.campitchunlock;

            num_overlap.Value = facemapdata.overlap;
            num_sidelap.Value = facemapdata.sidelap;
            
            rad_trigdist.Checked = facemapdata.trigdist;
            rad_digicam.Checked = facemapdata.digicam;
            rad_repeatservo.Checked = facemapdata.repeatservo;
            chk_stopstart.Checked = facemapdata.breaktrigdist;

            NUM_reptservo.Value = facemapdata.repeatservo_no;
            num_reptpwm.Value = facemapdata.repeatservo_pwm;
            NUM_repttime.Value = facemapdata.repeatservo_cycle;

            num_setservono.Value = facemapdata.setservo_no;
            num_setservolow.Value = facemapdata.setservo_low;
            num_setservohigh.Value = facemapdata.setservo_high;

            CHK_extraimages.Checked = facemapdata.extraimages;

            // Copter Settings
            NUM_copter_delay.Value = facemapdata.copter_delay;

            loadedfromfile = true;
        }

        FaceMapData saveFaceMapData()
        {
            FaceMapData facemapdata = new FaceMapData();

            facemapdata.poly = list;

            facemapdata.camera = CMB_camera.Text;
            facemapdata.benchheight = NUM_BenchHeight.Value;
            facemapdata.angle = NUM_angle.Value;
            facemapdata.facedirection = CHK_facedirection.Checked;
            facemapdata.speed = NUM_UpDownFlySpeed.Value;
            facemapdata.usespeed = CHK_usespeed.Checked;
            facemapdata.autotakeoff = CHK_toandland.Checked;
            facemapdata.autotakeoff_RTL = CHK_toandland_RTL.Checked;
            facemapdata.splitmission = NUM_split.Value;
            facemapdata.overlap = num_overlap.Value;
            facemapdata.sidelap = num_sidelap.Value;

            facemapdata.bermdepth = NUM_BermDepth.Value;
            facemapdata.numbenches = NUM_Benches.Value;
            facemapdata.camerapitch = NUM_cameraPitch.Value;
            facemapdata.toeheight = NUM_toeHeight.Value;
            facemapdata.campitchunlock = CHK_camPitchUnlock.Checked;

            facemapdata.extraimages = CHK_extraimages.Checked;

            // Copter Settings
            facemapdata.copter_delay = NUM_copter_delay.Value;

            facemapdata.trigdist = rad_trigdist.Checked;
            facemapdata.digicam = rad_digicam.Checked;
            facemapdata.repeatservo = rad_repeatservo.Checked;
            facemapdata.breaktrigdist = chk_stopstart.Checked;

            facemapdata.repeatservo_no = NUM_reptservo.Value;
            facemapdata.repeatservo_pwm = num_reptpwm.Value;
            facemapdata.repeatservo_cycle = NUM_repttime.Value;

            facemapdata.setservo_no = num_setservono.Value;
            facemapdata.setservo_low = num_setservolow.Value;
            facemapdata.setservo_high = num_setservohigh.Value;

            return facemapdata;
        }

        void loadsettings()
        {
            if (plugin.Host.config.ContainsKey("facemap_camera"))
            {
                loadsetting("facemap_benchheight", NUM_BenchHeight);
                loadsetting("facemap_facedir", CHK_facedirection);
                loadsetting("facemap_autotakeoff", CHK_toandland);
                loadsetting("facemap_autotakeoff_RTL", CHK_toandland_RTL);
                loadsetting("facemap_followpathhome", CHK_FollowPathHome);
                loadsetting("facemap_benchangle", NUM_angle);
                loadsetting("facemap_bermdepth", NUM_BermDepth);
                loadsetting("facemap_numbenches", NUM_Benches);
                loadsetting("facemap_campitch", NUM_cameraPitch);
                loadsetting("facemap_toeheight", NUM_toeHeight);
                loadsetting("facemap_unlockcampitch", CHK_camPitchUnlock);
                loadsetting("facemap_extraimages", CHK_extraimages);

                loadsetting("facemap_overlap", num_overlap);
                loadsetting("facemap_sidelap", num_sidelap);
                loadsetting("facemap_distance", NUM_Distance);
                loadsetting("facemap_usespeed", CHK_usespeed);
                loadsetting("facemap_speed", NUM_UpDownFlySpeed);

                loadsetting("facemap_trigdist", rad_trigdist);
                loadsetting("facemap_digicam", rad_digicam);
                loadsetting("facemap_repeatservo", rad_repeatservo);
                loadsetting("facemap_breakstopstart", chk_stopstart);

                loadsetting("facemap_repeatservo_no", NUM_reptservo);
                loadsetting("facemap_repeatservo_pwm", num_reptpwm);
                loadsetting("facemap_repeatservo_cycle", NUM_repttime);

                // camera last as it invokes a reload
                loadsetting("facemap_camera", CMB_camera);

                // Copter Settings
                loadsetting("facemap_copter_delay", NUM_copter_delay);
            }
        }

        void loadsetting(string key, Control item)
        {
            // soft fail on bad param
            try
            {
                if (plugin.Host.config.ContainsKey(key))
                {
                    if (item is NumericUpDown)
                    {
                        ((NumericUpDown)item).Value = decimal.Parse(plugin.Host.config[key].ToString());
                    }
                    else if (item is ComboBox)
                    {
                        ((ComboBox)item).Text = plugin.Host.config[key].ToString();
                    }
                    else if (item is CheckBox)
                    {
                        ((CheckBox)item).Checked = bool.Parse(plugin.Host.config[key].ToString());
                    }
                    else if (item is RadioButton)
                    {
                        ((RadioButton)item).Checked = bool.Parse(plugin.Host.config[key].ToString());
                    }
                }
            }
            catch { }
        }

        void savesettings()
        {
            plugin.Host.config["facemap_camera"] = CMB_camera.Text;
            plugin.Host.config["facemap_benchheight"] = NUM_BenchHeight.Value.ToString();
            plugin.Host.config["facemap_benchangle"] = NUM_angle.Value.ToString();
            plugin.Host.config["facemap_facedir"] = CHK_facedirection.Checked.ToString();
            plugin.Host.config["facemap_autotakeoff"] = CHK_toandland.Checked.ToString();
            plugin.Host.config["facemap_autotakeoff_RTL"] = CHK_toandland_RTL.Checked.ToString();
            plugin.Host.config["facemap_followpathhome"] = CHK_FollowPathHome.Checked.ToString();
            plugin.Host.config["facemap_bermdepth"] = NUM_BermDepth.Value.ToString();
            plugin.Host.config["facemap_numbenches"] = NUM_Benches.Value.ToString();
            plugin.Host.config["facemap_campitch"] = NUM_cameraPitch.Value.ToString();
            plugin.Host.config["facemap_toeheight"] = NUM_toeHeight.Value.ToString();
            plugin.Host.config["facemap_unlockcampitch"] = CHK_camPitchUnlock.Checked.ToString();
            plugin.Host.config["facemap_extraimages"] = CHK_extraimages.Checked.ToString();

            plugin.Host.config["facemap_usespeed"] = CHK_usespeed.Checked.ToString();
            plugin.Host.config["facemap_speed"] = NUM_UpDownFlySpeed.Value.ToString();
            plugin.Host.config["facemap_distance"] = NUM_Distance.Value.ToString();
            plugin.Host.config["facemap_overlap"] = num_overlap.Value.ToString();
            plugin.Host.config["facemap_sidelap"] = num_sidelap.Value.ToString();

            plugin.Host.config["facemap_trigdist"] = rad_trigdist.Checked.ToString();
            plugin.Host.config["facemap_digicam"] = rad_digicam.Checked.ToString();
            plugin.Host.config["facemap_repeatservo"] = rad_repeatservo.Checked.ToString();
            plugin.Host.config["facemap_breakstopstart"] = chk_stopstart.Checked.ToString();

            plugin.Host.config["facemap_repeatservo_no"] = NUM_reptservo.Value.ToString();
            plugin.Host.config["facemap_repeatservo_pwm"] = num_reptpwm.Value.ToString();
            plugin.Host.config["facemap_repeatservo_cycle"] = NUM_repttime.Value.ToString();

            // Copter Settings
            plugin.Host.config["facemap_copter_delay"] = NUM_copter_delay.Value.ToString();
        }

        private void xmlcamera(bool write, string filename)
        {
            bool exists = File.Exists(filename);

            if (write || !exists)
            {
                try
                {
                    XmlTextWriter xmlwriter = new XmlTextWriter(filename, Encoding.ASCII);
                    xmlwriter.Formatting = Formatting.Indented;

                    xmlwriter.WriteStartDocument();

                    xmlwriter.WriteStartElement("Cameras");

                    foreach (string key in cameras.Keys)
                    {
                        try
                        {
                            if (key == "")
                                continue;
                            xmlwriter.WriteStartElement("Camera");
                            xmlwriter.WriteElementString("name", cameras[key].name);
                            xmlwriter.WriteElementString("flen", cameras[key].focallen.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("imgh", cameras[key].imageheight.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("imgw", cameras[key].imagewidth.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("senh", cameras[key].sensorheight.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteElementString("senw", cameras[key].sensorwidth.ToString(new System.Globalization.CultureInfo("en-US")));
                            xmlwriter.WriteEndElement();
                        }
                        catch { }
                    }

                    xmlwriter.WriteEndElement();

                    xmlwriter.WriteEndDocument();
                    xmlwriter.Close();

                }
                catch (Exception ex) { CustomMessageBox.Show(ex.ToString()); }
            }
            else
            {
                try
                {
                    using (XmlTextReader xmlreader = new XmlTextReader(filename))
                    {
                        while (xmlreader.Read())
                        {
                            xmlreader.MoveToElement();
                            try
                            {
                                switch (xmlreader.Name)
                                {
                                    case "Camera":
                                        {
                                            camerainfo camera = new camerainfo();

                                            while (xmlreader.Read())
                                            {
                                                bool dobreak = false;
                                                xmlreader.MoveToElement();
                                                switch (xmlreader.Name)
                                                {
                                                    case "name":
                                                        camera.name = xmlreader.ReadString();
                                                        break;
                                                    case "imgw":
                                                        camera.imagewidth = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "imgh":
                                                        camera.imageheight = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "senw":
                                                        camera.sensorwidth = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "senh":
                                                        camera.sensorheight = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "flen":
                                                        camera.focallen = float.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                                        break;
                                                    case "Camera":
                                                        cameras[camera.name] = camera;
                                                        dobreak = true;
                                                        break;
                                                }
                                                if (dobreak)
                                                    break;
                                            }
                                            string temp = xmlreader.ReadString();
                                        }
                                        break;
                                    case "Config":
                                        break;
                                    case "xml":
                                        break;
                                    default:
                                        if (xmlreader.Name == "") // line feeds
                                            break;
                                        //config[xmlreader.Name] = xmlreader.ReadString();
                                        break;
                                }
                            }
                            catch (Exception ee) { Console.WriteLine(ee.Message); } // silent fail on bad entry
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("Bad Camera File: " + ex.ToString()); } // bad config file

                // populate list
                foreach (var camera in cameras.Values)
                {
                    if (!CMB_camera.Items.Contains(camera.name))
                        CMB_camera.Items.Add(camera.name);
                }
            }
        }

        // Do Work
        private void domainUpDown1_ValueChanged(object sender, EventArgs e)
        {
            int strips = 0;
            int images = 0;
            int a = 1;
            float routetotal = 0;
            List<PointLatLng> segment = new List<PointLatLng>();
            double maxgroundelevation = double.MinValue;
            double mingroundelevation = double.MaxValue;
            double startalt = plugin.Host.cs.HomeAlt;

            if (loading)
                return;

            if (!CHK_camPitchUnlock.Checked)
            {
                NUM_cameraPitch.Value = 90 - NUM_angle.Value;
            }

            if (CMB_camera.Text != "")
            {
                doCalc();
            }

            grid = FaceMap.CreateCorridor(list, CurrentState.fromDistDisplayUnit((double)NUM_BenchHeight.Value), (double)viewheight,
                     (double)camVerticalSpacing, (double)NUM_Distance.Value, (double)NUM_angle.Value, (double)NUM_cameraPitch.Value,
                     CHK_facedirection.Checked, (double)NUM_BermDepth.Value, (int)NUM_Benches.Value, (double)NUM_toeHeight.Value,
                     CHK_FollowPathHome.Checked, startalt, (FlightPlanner.altmode)plugin.Host.MainForm.FlightPlanner.CMB_altmode.SelectedValue);


            PointLatLngAlt prevprevpoint = grid[0];
            PointLatLngAlt prevpoint = grid[0];

            map.HoldInvalidation = true;

            routesOverlay.Routes.Clear();
            routesOverlay.Polygons.Clear();
            routesOverlay.Markers.Clear();

            GMapMarkerOverlap.Clear();

            if (CHK_boundary.Checked)
                AddDrawPolygon();

            foreach (var item in grid)
            {
                double currentalt = srtm.getAltitude(item.Lat, item.Lng).alt;
                mingroundelevation = Math.Min(mingroundelevation, currentalt);
                maxgroundelevation = Math.Max(maxgroundelevation, currentalt);

                prevprevpoint = prevpoint;

                if (item.Tag == "E")
                    strips++;

                if (CHK_markers.Checked)
                {
                    var marker = new GMapMarkerWP(item, a.ToString()) { ToolTipText = a.ToString(), ToolTipMode = MarkerTooltipMode.OnMouseOver };
                    routesOverlay.Markers.Add(marker);
                }

                segment.Add(prevpoint);
                segment.Add(item);
                prevpoint = item;
                a++;

                GMapRoute seg = new GMapRoute(segment, "segment" + a.ToString());
                seg.Stroke = new Pen(Color.Yellow, 4);
                seg.Stroke.DashStyle = System.Drawing.Drawing2D.DashStyle.Custom;
                seg.IsHitTestVisible = true;
                routetotal = routetotal + (float)seg.Distance;
                if (CHK_grid.Checked)
                {
                    routesOverlay.Routes.Add(seg);
                }
                else
                {
                    seg.Dispose();
                }

                segment.Clear();
            }

            // turn radrad = tas^2 / (tan(angle) * G)
            float v_sq = (float)(((float)NUM_UpDownFlySpeed.Value / CurrentState.multiplierspeed) * ((float)NUM_UpDownFlySpeed.Value / CurrentState.multiplierspeed));
            float turnrad = (float)(v_sq / (float)(9.808f * Math.Tan(35 * deg2rad)));

            // Update Stats 
            if (DistUnits == "Feet")
            {
                // Distance
                float distance = routetotal * 3280.84f; // Calculate the distance in feet
                if (distance < 5280f)
                {
                    lbl_distance.Text = distance.ToString("#") + " ft";
                }
                else
                {
                    distance = distance / 5280f;
                    lbl_distance.Text = distance.ToString("0.##") + " miles";
                }

                float alt = (float)grid[0].Alt * 3280.84f;
                //face width
                if (distance < 5280f)
                {
                    lbl_initialalt.Text = distance.ToString("0.##") + " ft";
                }
                else
                {
                    distance = distance / 5280f;
                    lbl_initialalt.Text = distance.ToString("0.##") + " miles";
                }

                lbl_spacing.Text = (NUM_spacing.Value * 3.2808399m).ToString("#") + " ft";
                lbl_grndres.Text = inchpixel;
                lbl_distbetweenlines.Text = (camVerticalSpacing * 3.2808399m).ToString("0.##") + " ft";
                lbl_footprint.Text = feet_fovH + " x " + feet_fovV + " ft";
                lbl_turnrad.Text = (turnrad * 2 * 3.2808399).ToString("0") + " ft";
                lbl_gndelev.Text = (mingroundelevation*3.2808399).ToString("0") + "-" + (maxgroundelevation*3.2808399).ToString("0") + " ft";
            }
            else
            {
                // Meters
                lbl_initialalt.Text = grid[0].Alt.ToString("0.##") + "m";
                lbl_spacing.Text = NUM_spacing.Value.ToString("#") + " m";
                lbl_distance.Text = routetotal.ToString("0.##") + " km";
                lbl_grndres.Text = TXT_cmpixel.Text;
                lbl_distbetweenlines.Text = camVerticalSpacing.ToString("0.##") + " m";
                lbl_footprint.Text = TXT_fovH.Text + " x " + TXT_fovV.Text + " m";
                lbl_turnrad.Text = (turnrad * 2).ToString("0") + " m";
                lbl_gndelev.Text = mingroundelevation.ToString("0") + "-" + maxgroundelevation.ToString("0") + " m";

            }

            try {
                // speed m/s
                var speed = ((float) NUM_UpDownFlySpeed.Value / CurrentState.multiplierspeed);
                // cmpix cm/pixel
                var cmpix = float.Parse(TXT_cmpixel.Text.TrimEnd(new[] {'c', 'm', ' '}));
                // m pix = m/pixel
                var mpix = cmpix * 0.01;
                // gsd / 2.0
                var minmpix = mpix / 2.0;
                // min sutter speed
                var minshutter = speed / minmpix;
                lbl_minshutter.Text = "1/"+(minshutter - minshutter % 1).ToString();
            }
            catch { }

            double flyspeedms = CurrentState.fromSpeedDisplayUnit((double)NUM_UpDownFlySpeed.Value);

            lbl_pictures.Text = ((int)(routetotal * 1000/ (float)NUM_spacing.Value)).ToString();
            lbl_strips.Text = ((int)strips).ToString();
            double seconds = ((routetotal * 1000.0) / ((flyspeedms) * 0.8));
            // reduce flying speed by 20 %
            lbl_flighttime.Text = secondsToNice(seconds);
            seconds = ((routetotal * 1000.0) / (flyspeedms));
            lbl_photoevery.Text = secondsToNice(((double)NUM_spacing.Value / flyspeedms));
            map.HoldInvalidation = false;
            if (!isMouseDown && sender != NUM_angle)
                map.ZoomAndCenterMarkers("routes");

            map.Invalidate();
            pictureBox1.Invalidate();
        }

        private void AddWP(double Lng, double Lat, double Alt, double bearing, double delay = 0, object gridobject = null, bool image_before_yaw = false, bool image_after_yaw = false)
        {
            // Delay between commands by whatever the maximum is of the number that the user entered in the delay box, or another hard coded delay.
            double max_delay = Math.Max(Math.Max((double)NUM_copter_delay.Value, 0), Math.Max(delay, 0));

            // If required, we capture an extra image before we adjust the heading.
            if (image_before_yaw && (bearing != -1))
            {
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_DIGICAM_CONTROL, 0, 0, 0, 0, 0, 1, 0, gridobject);
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DELAY, max_delay, 0, 0, 0, 0, 0, 0, gridobject);
            }

            if (bearing != -1)
            {
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.CONDITION_YAW, bearing, 0, 0, 0, 0, 0, 0, gridobject);
            }

            // If required, we capture an extra image after we adjust the heading.
            if (image_after_yaw && (bearing != -1))
            {
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_DIGICAM_CONTROL, 0, 0, 0, 0, 0, 1, 0, gridobject);
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DELAY, max_delay, 0, 0, 0, 0, 0, 0, gridobject);

                // Then you need to yaw AGAIN, because DELAY counts as a nav command, so the yaw will become unlocked again.
                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.CONDITION_YAW, bearing, 0, 0, 0, 0, 0, 0, gridobject);
            }

            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.WAYPOINT, max_delay, 0, 0, 0, Lng, Lat, Alt * CurrentState.multiplierdist, gridobject);
        }

        string secondsToNice(double seconds)
        {
            if (seconds < 0)
                return "Infinity Seconds";

            double secs = seconds % 60;
            int mins = (int)(seconds / 60) % 60;
            int hours = (int)(seconds / 3600) % 24;

            if (hours > 0)
            {
                return hours + ":" + mins.ToString("00") + ":" + secs.ToString("00") + " Hours";
            }
            else if (mins > 0)
            {
                return mins + ":" + secs.ToString("00") + " Minutes";
            }
            else
            {
                return secs.ToString("0.00") + " Seconds";
            }
        }

        void AddDrawPolygon()
        {
            List<PointLatLng> list2 = new List<PointLatLng>();

            list.ForEach(x => { list2.Add(x); });

            var poly = new GMapPolygon(list2, "poly");
            poly.Stroke = new Pen(Color.Red, 2);
            poly.Fill = Brushes.Transparent;

            routesOverlay.Polygons.Add(poly);

            foreach (var item in list)
            {
                routesOverlay.Markers.Add(new GMarkerGoogle(item, GMarkerGoogleType.red));
            }
        }

        void getFOV(double flyalt, ref double fovh, ref double fovv)
        {
            double focallen = (double)NUM_focallength.Value;
            double sensorwidth = double.Parse(TXT_senswidth.Text);
            double sensorheight = double.Parse(TXT_sensheight.Text);

            // scale      mm / mm
            double flscale = (1000 * flyalt) / focallen;

            //   mm * mm / 1000
            double viewwidth = (sensorwidth * flscale / 1000);
            double viewheight = (sensorheight * flscale / 1000);

            float fovh1 = (float)(Math.Atan(sensorwidth / (2 * focallen)) * rad2deg * 2);
            float fovv1 = (float)(Math.Atan(sensorheight / (2 * focallen)) * rad2deg * 2);

            fovh = viewwidth;
            fovv = viewheight;
        }

        void getFOVangle(ref double fovh, ref double fovv)
        {
            double focallen = (double)NUM_focallength.Value;
            double sensorwidth = double.Parse(TXT_senswidth.Text);
            double sensorheight = double.Parse(TXT_sensheight.Text);

            fovh = (float)(Math.Atan(sensorwidth / (2 * focallen)) * rad2deg * 2);
            fovv = (float)(Math.Atan(sensorheight / (2 * focallen)) * rad2deg * 2);
        }

        void doCalc()
        {
            try
            {
                // entered values
                float distanceFromFace = (float)CurrentState.fromDistDisplayUnit((float)NUM_Distance.Value);
                int imagewidth = int.Parse(TXT_imgwidth.Text);
                int imageheight = int.Parse(TXT_imgheight.Text);
                
                int overlap = (int)num_overlap.Value;
                int sidelap = (int)num_sidelap.Value;

                getFOV(distanceFromFace, ref viewwidth, ref viewheight);

                TXT_fovH.Text = viewwidth.ToString("#.#");
                TXT_fovV.Text = viewheight.ToString("#.#");
                // Imperial
                feet_fovH = (viewwidth * 3.2808399f).ToString("#.#");
                feet_fovV = (viewheight * 3.2808399f).ToString("#.#");

                //    mm  / pixels * 100
                TXT_cmpixel.Text = ((viewheight / imageheight) * 100).ToString("0.00 cm");
                // Imperial
                inchpixel = (((viewheight / imageheight) * 100) * 0.393701).ToString("0.00 inches");

                NUM_spacing.ValueChanged -= domainUpDown1_ValueChanged;

                NUM_spacing.Value = (decimal)((1 - (sidelap / 100.0f)) * viewwidth);
                camVerticalSpacing = (decimal)((1 - (overlap / 100.0f)) * viewheight);

                NUM_spacing.ValueChanged += domainUpDown1_ValueChanged;
            }
            catch { return; }
        }


        // Map Operators
        private void map_OnRouteEnter(GMapRoute item)
        {
            string dist;
            if (DistUnits == "Feet")
            {
                dist = ((float)item.Distance * 3280.84f).ToString("0.##") + " ft";
            }
            else
            {
                dist = ((float)item.Distance * 1000f).ToString("0.##") + " m";
            }
            if (marker != null)
            {
                if (routesOverlay.Markers.Contains(marker))
                    routesOverlay.Markers.Remove(marker);
            }

            PointLatLng point = currentMousePosition;

            marker = new GMapMarkerRect(point);
            marker.ToolTip = new GMapToolTip(marker);
            marker.ToolTipMode = MarkerTooltipMode.Always;
            marker.ToolTipText = "Line: " + dist;
            routesOverlay.Markers.Add(marker);
        }

        private void map_OnRouteLeave(GMapRoute item)
        {
            if (marker != null)
            {
                try
                {
                    if (routesOverlay.Markers.Contains(marker))
                        routesOverlay.Markers.Remove(marker);
                }
                catch { }
            }
        }

        private void map_OnMarkerLeave(GMapMarker item)
        {
            if (!isMouseDown)
            {
                if (item is GMapMarker)
                {
                    // when you click the context menu this triggers and causes problems
                    CurrentGMapMarker = null;
                }

            }
        }

        private void map_OnMarkerEnter(GMapMarker item)
        {
            if (!isMouseDown)
            {
                if (item is GMapMarker)
                {
                    CurrentGMapMarker = item as GMapMarker;
                    CurrentGMapMarkerStartPos = CurrentGMapMarker.Position;
                }
            }
        }

        private void map_MouseUp(object sender, MouseEventArgs e)
        {
            MouseDownEnd = map.FromLocalToLatLng(e.X, e.Y);

            // Console.WriteLine("MainMap MU");

            if (e.Button == MouseButtons.Right) // ignore right clicks
            {
                return;
            }

            if (isMouseDown) // mouse down on some other object and dragged to here.
            {
                if (e.Button == MouseButtons.Left)
                {
                    isMouseDown = false;
                }
                if (!isMouseDraging)
                {
                    if (CurrentGMapMarker != null)
                    {
                        // Redraw polygon
                        //AddDrawPolygon();
                    }
                }
            }
            isMouseDraging = false;
            CurrentGMapMarker = null;
            CurrentGMapMarkerIndex = 0;
            CurrentGMapMarkerStartPos = null;
        }

        private void map_MouseDown(object sender, MouseEventArgs e)
        {
            MouseDownStart = map.FromLocalToLatLng(e.X, e.Y);

            if (e.Button == MouseButtons.Left && Control.ModifierKeys != Keys.Alt)
            {
                isMouseDown = true;
                isMouseDraging = false;

                if (CurrentGMapMarkerStartPos != null)
                    CurrentGMapMarkerIndex = list.FindIndex(c => c.ToString() == CurrentGMapMarkerStartPos.ToString());
            }
        }

        private void map_MouseMove(object sender, MouseEventArgs e)
        {
            PointLatLng point = map.FromLocalToLatLng(e.X, e.Y);
            currentMousePosition = point;

            if (MouseDownStart == point)
                return;

            if (!isMouseDown)
            {
                // update mouse pos display
                //SetMouseDisplay(point.Lat, point.Lng, 0);
            }

            //draging
            if (e.Button == MouseButtons.Left && isMouseDown)
            {
                isMouseDraging = true;

                if (CurrentGMapMarker != null)
                {
                    if (CurrentGMapMarkerIndex == -1)
                    {
                        isMouseDraging = false;
                        return;
                    }

                    PointLatLng pnew = map.FromLocalToLatLng(e.X, e.Y);

                    CurrentGMapMarker.Position = pnew;

                    list[CurrentGMapMarkerIndex] = new PointLatLngAlt(pnew);
                    domainUpDown1_ValueChanged(sender, e);
                }
                else // left click pan
                {
                    double latdif = MouseDownStart.Lat - point.Lat;
                    double lngdif = MouseDownStart.Lng - point.Lng;

                    try
                    {
                        lock (thisLock)
                        {
                            map.Position = new PointLatLng(map.Position.Lat + latdif, map.Position.Lng + lngdif);
                        }
                    }
                    catch { }
                }
            }
        }

        private void map_OnMapZoomChanged()
        {
            if (map.Zoom > 0)
            {
                try
                {
                    TRK_zoom.Value = (float)map.Zoom;
                }
                catch { }
            }
        }

        // Operators
        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                lock (thisLock)
                {
                    map.Zoom = TRK_zoom.Value;
                }
            }
            catch { }
        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            try
            {
                lock (thisLock)
                {
                    map.Zoom = TRK_zoom.Value;
                }
            }
            catch { }
        }

        private void NUM_ValueChanged(object sender, EventArgs e)
        {
            domainUpDown1_ValueChanged(null,null);
        }

        private void CMB_camera_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cameras.ContainsKey(CMB_camera.Text))
            {
                camerainfo camera = cameras[CMB_camera.Text];

                NUM_focallength.Value = (decimal)camera.focallen;
                TXT_imgheight.Text = camera.imageheight.ToString();
                TXT_imgwidth.Text = camera.imagewidth.ToString();
                TXT_sensheight.Text = camera.sensorheight.ToString();
                TXT_senswidth.Text = camera.sensorwidth.ToString();
            }

            GMapMarkerOverlap.Clear();

            domainUpDown1_ValueChanged(null, null);
        }

        private void TXT_TextChanged(object sender, EventArgs e)
        {
            domainUpDown1_ValueChanged(null, null);
        }

        private void CHK_camdirection_CheckedChanged(object sender, EventArgs e)
        {
            domainUpDown1_ValueChanged(null, null);
        }

        private void BUT_samplephoto_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "*.jpg|*.jpg";

                ofd.ShowDialog();

                if (File.Exists(ofd.FileName))
                {
                    string fn = ofd.FileName;

                    Metadata lcMetadata = null;
                    try
                    {
                        FileInfo lcImgFile = new FileInfo(fn);
                        // Loading all meta data
                        lcMetadata = JpegMetadataReader.ReadMetadata(lcImgFile);

                        if (lcMetadata == null)
                            return;
                    }
                    catch (JpegProcessingException ex)
                    {
                        log.InfoFormat(ex.Message);
                        return;
                    }

                    foreach (AbstractDirectory lcDirectory in lcMetadata)
                    {
                        foreach (var tag in lcDirectory)
                        {
                            Console.WriteLine(lcDirectory.GetName() + " - " + tag.GetTagName() + " " + tag.GetTagValue().ToString());
                        }

                        if (lcDirectory.ContainsTag(ExifDirectory.TAG_EXIF_IMAGE_HEIGHT))
                        {
                            TXT_imgheight.Text = lcDirectory.GetInt(ExifDirectory.TAG_EXIF_IMAGE_HEIGHT).ToString();
                        }

                        if (lcDirectory.ContainsTag(ExifDirectory.TAG_EXIF_IMAGE_WIDTH))
                        {
                            TXT_imgwidth.Text = lcDirectory.GetInt(ExifDirectory.TAG_EXIF_IMAGE_WIDTH).ToString();
                        }

                        if (lcDirectory.ContainsTag(ExifDirectory.TAG_FOCAL_PLANE_X_RES))
                        {
                            var unit = lcDirectory.GetFloat(ExifDirectory.TAG_FOCAL_PLANE_UNIT);

                            // TXT_senswidth.Text = lcDirectory.GetDouble(ExifDirectory.TAG_FOCAL_PLANE_X_RES).ToString();
                        }

                        if (lcDirectory.ContainsTag(ExifDirectory.TAG_FOCAL_PLANE_Y_RES))
                        {
                            var unit = lcDirectory.GetFloat(ExifDirectory.TAG_FOCAL_PLANE_UNIT);

                            // TXT_sensheight.Text = lcDirectory.GetDouble(ExifDirectory.TAG_FOCAL_PLANE_Y_RES).ToString();
                        }

                        if (lcDirectory.ContainsTag(ExifDirectory.TAG_FOCAL_LENGTH))
                        {
                            try
                            {
                                var item = lcDirectory.GetFloat(ExifDirectory.TAG_FOCAL_LENGTH);
                                NUM_focallength.Value = (decimal)item;
                            }
                            catch { }
                        }


                        if (lcDirectory.ContainsTag(ExifDirectory.TAG_DATETIME_ORIGINAL))
                        {

                        }

                    }
                }
            }
        }

        private void BUT_save_Click(object sender, EventArgs e)
        {
            camerainfo camera = new camerainfo();

            string camname = "Default";

            if (MissionPlanner.Controls.InputBox.Show("Camera Name", "Please and a camera name", ref camname) != System.Windows.Forms.DialogResult.OK)
                return;

            CMB_camera.Text = camname;

            // check if camera exists alreay
            if (cameras.ContainsKey(CMB_camera.Text))
            {
                camera = cameras[CMB_camera.Text];
            }
            else
            {
                cameras.Add(CMB_camera.Text, camera);
            }

            try
            {
                camera.name = CMB_camera.Text;
                camera.focallen = (float)NUM_focallength.Value;
                camera.imageheight = float.Parse(TXT_imgheight.Text);
                camera.imagewidth = float.Parse(TXT_imgwidth.Text);
                camera.sensorheight = float.Parse(TXT_sensheight.Text);
                camera.sensorwidth = float.Parse(TXT_senswidth.Text);
            }
            catch { CustomMessageBox.Show("One of your entries is not a valid number"); return; }

            cameras[CMB_camera.Text] = camera;

            xmlcamera(true, Settings.GetUserDataDirectory() + "cameras.xml");
        }

        private void BUT_Accept_Click(object sender, EventArgs e)
        {
            double entryAltitude = 10;
            double exitAltitude = 10;

            if (grid != null && grid.Count > 0)
            {
                MainV2.instance.FlightPlanner.quickadd = true;

                if (NUM_split.Value > 1 && CHK_toandland.Checked != true)
                {
                    CustomMessageBox.Show("You must use Land/RTL to split a mission", Strings.ERROR);
                    return;
                }

                var gridobject = saveFaceMapData();

                int wpsplit = (int)Math.Round(grid.Count / NUM_split.Value,MidpointRounding.AwayFromZero);

                List<int> wpsplitstart = new List<int>();

                for (int splitno = 0; splitno < NUM_split.Value; splitno++)
                {
                    int wpstart = wpsplit*splitno;
                    int wpend = wpsplit*(splitno + 1);

                    // If planning in absolute mode.
                    if ((FlightPlanner.altmode)plugin.Host.MainForm.FlightPlanner.CMB_altmode.SelectedValue == FlightPlanner.altmode.Absolute)
                    {
                        // TODO - Restore me!

                        //exitAltitude = entryAltitude = plugin.Host.cs.HomeAlt + 10;
                    }

                    while (wpstart != 0 && wpstart < grid.Count && grid[wpstart].Tag != "E")
                    {
                        wpstart--;
                    }

                    while (wpend > 0 && wpend < grid.Count && grid[wpend].Tag != "S")
                    {
                        wpend--;
                    }

                    /* If the first surveying point is above the home location, fly to the entry height above this altitude before starting the survey run,
                       otherwise stay at the entry altitude. */

                    // If planning in absolute mode.
                    if ((FlightPlanner.altmode)plugin.Host.MainForm.FlightPlanner.CMB_altmode.SelectedValue == FlightPlanner.altmode.Absolute)
                    {
                        if (plugin.Host.cs.HomeAlt < grid[wpstart].Alt) entryAltitude += grid[wpstart].Alt;
                        else entryAltitude += plugin.Host.cs.HomeAlt;
                    }
                    else
                    {
                        if (grid[wpstart].Alt > 0) entryAltitude += grid[wpstart].Alt;
                    }

                    if (CHK_toandland.Checked)
                    {
                        if (plugin.Host.cs.firmware == Firmwares.ArduCopter2)
                        {
                            var wpno = plugin.Host.AddWPtoList(MAVLink.MAV_CMD.TAKEOFF, 20, 0, 0, 0, 0, 0, (entryAltitude * CurrentState.multiplierdist), gridobject);

                            wpsplitstart.Add(wpno);
                        }
                        else
                        {
                            var wpno = plugin.Host.AddWPtoList(MAVLink.MAV_CMD.TAKEOFF, 20, 0, 0, 0, 0, 0, (entryAltitude * CurrentState.multiplierdist), gridobject);

                            wpsplitstart.Add(wpno);
                        }
                    }

                    // Create waypoint to first point, flying at a safe altitude.
                    AddWP(grid[0].Lng, grid[0].Lat, entryAltitude, -1);

                    if (CHK_usespeed.Checked)
                    {
                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_CHANGE_SPEED, 0,
                            (int) ((float) NUM_UpDownFlySpeed.Value/CurrentState.multiplierspeed), 0, 0, 0, 0, 0,
                            gridobject);
                    }

                    plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_MOUNT_CONTROL, (double)NUM_cameraPitch.Value * -1, 0, 0, 0, 0, 0, 0, MAVLink.MAV_MOUNT_MODE.MAVLINK_TARGETING);

                    int i = 0;
                    bool startedtrigdist = false;
                    int direction = (CHK_facedirection.Checked == true ? -1 : 1);
                    double faceHeading = 0;
                    PointLatLngAlt lastplla = PointLatLngAlt.Zero;
                    foreach (var plla in grid)
                    {
                        // skip before start point
                        if (i < wpstart)
                        {
                            i++;
                            continue;
                        }
                        // skip after endpoint
                        if (i >= wpend)
                            break;
                        if (i > wpstart)
                        {
                            /* calculate the heading of the aircraft in order to face the created path. Ignore paths from End tags to Start tags
                             (transitioning between each lane) and the very first point in the path.
                             positive offset from path is defined as to the port side of the aircraft so yaw 90 anticlockwise
                             to face the path on Odd lanes. Even lanes are calculated in the opposite direction so must be rotated
                             90 clockwise instead.*/
                            if (plla.Tag != "S" && lastplla.Tag != "")
                            {
                                if (plla.Lat != lastplla.Lat || plla.Lng != lastplla.Lng)
                                {
                                    faceHeading = AddAngle(ComputeBearing(lastplla, plla), (-90 * direction));
                                }
                            }

                            //at the end of each lane the path follows the opposite direction, update direction value to get correct heading
                            if (plla.Tag == "E")
                            {
                                direction = -direction;
                            }

                            // If we're just arrived at the last waypoint, snap one more pic.
                            if (plla.Tag == "R" && lastplla.Tag != "R" && CHK_extraimages.Checked)
                            {
                                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_DIGICAM_CONTROL, 0, 0, 0, 0, 0, 1, 0, gridobject);
                                plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DELAY, (double)NUM_copter_delay.Value, 0, 0, 0, 0, 0, 0, gridobject);
                            }

                            // points that do not trigger the camera

                            if (plla.Lat != lastplla.Lat || plla.Lng != lastplla.Lng ||
                                plla.Alt != lastplla.Alt)
                            {
                                switch (plla.Tag)
                                {
                                    case "M":
                                        if (lastplla.Tag == "S" || lastplla.Tag == "SM") AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, image_after_yaw: CHK_extraimages.Checked);
                                        else AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, image_before_yaw: CHK_extraimages.Checked, image_after_yaw: CHK_extraimages.Checked);
                                        break;
                                    case "S":
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, 1, image_before_yaw: CHK_extraimages.Checked);
                                        break;
                                    case "E":
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, 1);
                                        break;
                                    case "R":
                                        //turn off camera and fly without strafing the face as an indication that the mission is complete on return path
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, -1);

                                        if (rad_trigdist.Checked && startedtrigdist)
                                        {
                                            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, 0, 0, 0, 0, 0, 0, 0, gridobject);
                                            startedtrigdist = false;
                                        }
                                        break;
                                    default:
                                        break;
                                }
                            }

                            // check trigger method
                            if (rad_trigdist.Checked)
                            {
                                // if stopstart enabled, add wp and trigger start/stop
                                if (chk_stopstart.Checked)
                                {
                                    if (plla.Tag == "SM")
                                    {
                                        //  s > sm, need to dup check
                                        if (plla.Lat != lastplla.Lat || plla.Lng != lastplla.Lng || plla.Alt != lastplla.Alt)
                                            AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, image_after_yaw: CHK_extraimages.Checked);

                                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, (float) NUM_spacing.Value, 0, 0, 0, 0, 0, 0, gridobject);
                                        startedtrigdist = true;
                                    }
                                    else if (plla.Tag == "ME")
                                    {
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, 1, image_before_yaw: CHK_extraimages.Checked, image_after_yaw: CHK_extraimages.Checked);

                                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, 0, 0, 0, 0, 0, 0, 0, gridobject);
                                        startedtrigdist = false;
                                    }
                                }
                                else
                                {
                                    // add single start trigger
                                    if (!startedtrigdist)
                                    {
                                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, (float) NUM_spacing.Value, 0, 0, 0, 0, 0, 0, gridobject);
                                        startedtrigdist = true;
                                    }
                                    else if (plla.Tag == "ME")
                                    {
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, 1);
                                    }
                                }
                            }
                            else if (rad_repeatservo.Checked)
                            {
                                if (chk_stopstart.Checked)
                                {
                                    if (plla.Tag == "SM")
                                    {
                                        if (plla.Lat != lastplla.Lat || plla.Lng != lastplla.Lng ||
                                            plla.Alt != lastplla.Alt)
                                            AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading);

                                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_REPEAT_SERVO,
                                            (float) NUM_reptservo.Value,
                                            (float) num_reptpwm.Value, 999, (float) NUM_repttime.Value, 0, 0, 0,
                                            gridobject);
                                    }
                                    else if (plla.Tag == "ME")
                                    {
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, 1);

                                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_REPEAT_SERVO,
                                            (float) NUM_reptservo.Value,
                                            (float) num_reptpwm.Value, 0, (float) NUM_repttime.Value, 0, 0, 0,
                                            gridobject);
                                    }
                                }
                            }
                            else if (rad_do_set_servo.Checked)
                            {
                                if (plla.Tag == "SM")
                                {
                                    if (plla.Lat != lastplla.Lat || plla.Lng != lastplla.Lng ||
                                        plla.Alt != lastplla.Alt)
                                        AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading);

                                    plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_SERVO,
                                        (float) num_setservono.Value,
                                        (float) num_setservolow.Value, 0, 0, 0, 0, 0,
                                        gridobject);
                                }
                                else if (plla.Tag == "ME")
                                {
                                    AddWP(plla.Lng, plla.Lat, plla.Alt, faceHeading, 1);

                                    plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_SERVO,
                                        (float) num_setservono.Value,
                                        (float) num_setservohigh.Value, 0, 0, 0, 0, 0,
                                        gridobject);
                                }
                            }
                        }
                        else
                        {
                            AddWP(plla.Lng, plla.Lat, plla.Alt, -1);
                        }
                        lastplla = plla;
                        ++i;
                    }

                    // end
                    if (rad_trigdist.Checked && startedtrigdist)
                    {
                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_SET_CAM_TRIGG_DIST, 0, 0, 0, 0, 0, 0, 0, gridobject);
                        startedtrigdist = false;
                    }
                    // If we're just arrived at the last waypoint, snap one more pic.
                    if (!CHK_FollowPathHome.Checked && CHK_extraimages.Checked)
                    {
                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_DIGICAM_CONTROL, 0, 0, 0, 0, 0, 1, 0, gridobject);
                        plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DELAY, (double) NUM_copter_delay.Value, 0, 0, 0, 0, 0, 0, gridobject);
                    }

                    //reset gimbal pitch for landing
                    plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_MOUNT_CONTROL, 0, 0, 0, 0, 0, 0, 0, MAVLink.MAV_MOUNT_MODE.MAVLINK_TARGETING);

                    if (CHK_usespeed.Checked)
                    {
                        if (MainV2.comPort.MAV.param["WPNAV_SPEED"] != null)
                        {
                            double speed = MainV2.comPort.MAV.param["WPNAV_SPEED"].Value;
                            speed = speed/100;
                            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.DO_CHANGE_SPEED, 0, speed, 0, 0, 0, 0, 0, gridobject);
                        }
                    }

                    if (CHK_toandland.Checked)
                    {
                        if (CHK_toandland_RTL.Checked)
                        {
                            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.RETURN_TO_LAUNCH, 0, 0, 0, 0, 0, 0, 0, gridobject);
                        }
                        else
                        {
                            // overwrite exit altitude if it is greater than 10m from home
                            if (exitAltitude < lastplla.Alt)
                            {
                                exitAltitude = lastplla.Alt;
                            }
                            else
                            {
                                //climb directly upwards to 10m above home if last wp alt is lower than that
                                AddWP(lastplla.Lng, lastplla.Lat, exitAltitude, -1);
                            }

                            //fly home at constant alt before landing
                            AddWP(plugin.Host.cs.HomeLocation.Lng, plugin.Host.cs.HomeLocation.Lat, exitAltitude, -1);

                            plugin.Host.AddWPtoList(MAVLink.MAV_CMD.LAND, 0, 0, 0, 0, plugin.Host.cs.HomeLocation.Lng,
                                plugin.Host.cs.HomeLocation.Lat, 0, gridobject);
                        }
                    }
                }

                if (NUM_split.Value > 1)
                {
                    int index = 0;
                    foreach (var i in wpsplitstart)
                    {
                        // add do jump
                        plugin.Host.InsertWP(index, MAVLink.MAV_CMD.DO_JUMP, i + wpsplitstart.Count + 1, 1, 0, 0, 0, 0, 0, gridobject);
                        index++;
                    }
                    
                }

                // Redraw the polygon in FP
                plugin.Host.RedrawFPPolygon(list);

                // save camera fov's for use with footprints
                double fovha = 0;
                double fovva = 0;
                try
                {
                    getFOVangle(ref fovha, ref fovva);
                    Settings.Instance["camera_fovh"] = fovva.ToString();
                    Settings.Instance["camera_fovv"] = fovha.ToString();
                }
                catch(Exception ex)
                {
                    log.Error(ex);
                }

                savesettings();

                MainV2.instance.FlightPlanner.quickadd = false;

                MainV2.instance.FlightPlanner.writeKML();

                this.Close();
            }
            else
            {
                CustomMessageBox.Show("Bad Grid", "Error");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O))
            {
                LoadFaceMap();

                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveFaceMap();

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void NUM_Lane_Dist_ValueChanged(object sender, EventArgs e)
        {
            // doCalc
            domainUpDown1_ValueChanged(sender, e);
        }

        //computes the bearing between two coordinates using haversine formula
        static double ComputeBearing(PointLatLngAlt start, PointLatLngAlt end)
        {
            var lat1 = start.Lat;
            var long1 = start.Lng;
            var lat2 = end.Lat;
            var long2 = end.Lng;

            var y = Math.Sin(deg2rad * (long2 - long1)) * Math.Cos(deg2rad * (lat2));
            var x = Math.Cos(deg2rad * (lat1)) * Math.Sin(deg2rad * (lat2)) - Math.Sin(deg2rad * (lat1)) * Math.Cos(deg2rad * (lat2)) * Math.Cos(deg2rad * (long2 - long1));

            var bearing = Math.Atan2(y, x);
            bearing *= rad2deg;

            return bearing;
        }
        
        // Add an angle while normalizing output in the range 0...360
        static double AddAngle(double angle, double degrees)
        {
            angle += degrees;

            angle = angle % 360;

            while (angle < 0)
            {
                angle += 360;
            }
            return angle;
        }

        private void CHK_camPitchUnlock_CheckedChanged(object sender, EventArgs e)
        {
            NUM_cameraPitch.Enabled = CHK_camPitchUnlock.Checked;
            if (!CHK_camPitchUnlock.Checked)
            {
                NUM_cameraPitch.Value = 90 - NUM_angle.Value;
            }
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.GhostWhite);
            
            //fill in sky
            RectangleF bg = new RectangleF(0, 0, pictureBox1.Width, pictureBox1.Height);

            using (System.Drawing.Drawing2D.LinearGradientBrush linearBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                bg, Color.FromArgb(128, Color.Blue), Color.LightBlue, System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                g.FillRectangle(linearBrush, bg);
            }

            //precalculate useful variables
            var tanAngle = Math.Tan((double)NUM_angle.Value * deg2rad);
            var vertIncrement = (double)camVerticalSpacing * Math.Sin((double)NUM_angle.Value * deg2rad);

            //calculate vert/horiz scaling factors
            double CopterDistX = (double)NUM_Distance.Value * Math.Cos((double)NUM_cameraPitch.Value * deg2rad);
            double CopterDistY = (double)NUM_Distance.Value * Math.Sin((double)NUM_cameraPitch.Value * deg2rad);
            double BenchDepth = (double)NUM_BenchHeight.Value / tanAngle;

            double totalWidth = (float)CopterDistX + (double)NUM_Benches.Value * ((double)NUM_BermDepth.Value + BenchDepth);
            double totalHeight = (double)NUM_Benches.Value * ((double)NUM_BenchHeight.Value) + CopterDistY + vertIncrement / 2;

            double scaleFactor = 390 / Math.Max(totalWidth, totalHeight);

            //draw ground from 0,0
            List<PointF> points = new List<PointF>();

            points.Add(new PointF(0, pictureBox1.Height));
            points.Add(new PointF(0, pictureBox1.Height - 30));

            //create toe point so benches are right aligned
            points.Add(new PointF(pictureBox1.Width - 30 - (float)((double)NUM_Benches.Value * ((double)NUM_BermDepth.Value + BenchDepth) * scaleFactor), pictureBox1.Height - 30));

            for (int bench = 1; bench <= NUM_Benches.Value; bench++)
            {
                //slope
                points.Add(new PointF((float)(BenchDepth * scaleFactor) + points.Last().X,
                    pictureBox1.Height - 30 - ((float)(bench * (double)NUM_BenchHeight.Value * scaleFactor))));

                //berm
                points.Add(new PointF(points.Last().X + (float)((double)NUM_BermDepth.Value * scaleFactor), points.Last().Y));
            }

            //close polygon
            points.Add(new PointF(pictureBox1.Width, points.Last().Y));
            points.Add(new PointF(pictureBox1.Width, pictureBox1.Height));

            //fill in bench polygon
            using (System.Drawing.Drawing2D.LinearGradientBrush linearBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                     bg, Color.FromArgb(0x9b, 0xb8, 0x24), Color.FromArgb(0x41, 0x4f, 0x07), System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                g.FillPolygon(linearBrush, points.ToArray());
            }

            if (camVerticalSpacing != 0)
            {
                double initialAltitude = viewheight * Math.Sin((double)NUM_angle.Value* deg2rad) / 3;
                var lanes = Math.Round(((double)NUM_BenchHeight.Value - initialAltitude) / vertIncrement) + 1;
                System.Drawing.Pen pen1 = new System.Drawing.Pen(Color.DimGray, 2F);
                pen1.DashPattern = new float[] { 2.0F, 2.0F };

                System.Drawing.SolidBrush transparentFill = new System.Drawing.SolidBrush(Color.FromArgb(48 ,Color.LightGoldenrodYellow));

                //draw points where each lane is relative to benches

                //repeat for each bench, applying height/berm depth offsets
                for (int bench = 0; bench < NUM_Benches.Value; bench++)
                {
                    //repeat for each increment up face
                    for (int lane = 0; lane < lanes; lane++)
                    {
                        //calculate offset from the base of the face based on toe angle, camera pitch, camera overlap % and bench offset
                        PointF surveyPoint = new PointF();
                        surveyPoint.X = (float)(points[2].X + (((initialAltitude + (lane * vertIncrement)) / tanAngle) + bench * ((double)NUM_BermDepth.Value + (double)NUM_BenchHeight.Value / tanAngle)) * scaleFactor);
                        surveyPoint.Y = (float)(points[2].Y - (initialAltitude + (lane * vertIncrement) + (bench * (double)NUM_BenchHeight.Value)) * scaleFactor);

                        PointF copterPoint = PointF.Add(surveyPoint, new SizeF((float)(-CopterDistX * scaleFactor), (float)(-CopterDistY * scaleFactor)));

                        //draw dotted line
                        g.DrawLine(pen1, surveyPoint, copterPoint);

                        //draw point at copter position
                        g.DrawImage(Resources.camera_icon_G, copterPoint.X - 10, copterPoint.Y - 10, 20, 20);

                        //draw triangular beams to represent FOV
                        PointF[] fovPoly = new PointF[3];
                        fovPoly[0] = copterPoint;

                        //intersection point above beam centre
                        fovPoly[1] = PointF.Add(surveyPoint, new SizeF((float)(viewheight/2 * Math.Cos((double)NUM_angle.Value * deg2rad) * scaleFactor), 
                            (float)(-viewheight/2 * Math.Sin((double)NUM_angle.Value * deg2rad) * scaleFactor)));

                        //intersection point below beam centre
                        fovPoly[2] = PointF.Add(surveyPoint, new SizeF((float)(-viewheight/2 * Math.Cos((double)NUM_angle.Value * deg2rad) * scaleFactor), 
                            (float)(viewheight/2 * Math.Sin((double)NUM_angle.Value * deg2rad) * scaleFactor)));

                        g.FillPolygon(transparentFill, fovPoly);
                    }
                }
                pen1.Dispose();
                transparentFill.Dispose();
            }
        }
    }
}