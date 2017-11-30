using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using GMap.NET;
using System.Xml;
using MissionPlanner.Utilities; // GE xml alt reader




namespace MissionPlanner.Controls
{
    public class TerrainElevation
    {
        List<PointLatLngAlt> gelocs = new List<PointLatLngAlt>();
        List<PointLatLngAlt> srtmlocs = new List<PointLatLngAlt>();
        List<PointLatLngAlt> planlocs = new List<PointLatLngAlt>();
        List<PointLatLngAlt> pointslist = new List<PointLatLngAlt>();
        //List<Point> points = new List<Point>();

        int distance = 0;

        public TerrainElevation(List<Point> points, float _heading, double _lat, double _lng, int width, int height)
        {
            create_pointslist(pointslist, _heading, _lat, _lng);

            // get total distance
            distance = 0;
            PointLatLngAlt lastloc = null;
            foreach (PointLatLngAlt loc in pointslist)
            {
                if (loc == null)
                    continue;

                if (lastloc != null)
                {
                    distance += (int)loc.GetDistance(lastloc);
                }
                lastloc = loc;
            }

            //gelocs = getGEAltPath(pointslist);    //Google Earth data
            gelocs = getSRTMAltPath(pointslist); //DEM data
            int disttotal = 0;
            if (gelocs.Count != 0)
            {
                var prevloc = gelocs[0];
                //convert pointslist into distance, altitude points
                foreach (PointLatLngAlt loc in gelocs)
                {
                    int b = (height - (int)loc.Alt);
                    points.Add(new Point(disttotal, b));
                    disttotal += width / (gelocs.Count);
                    prevloc = loc;
                }
                points.Add(new Point(disttotal, height - (int)prevloc.Alt));
            }
        }

        double mod(double a, double n)
        {
            double result = a % n;
            if ((a < 0 && n > 0) || (a > 0 && n < 0))
                result += n;
            return result;
        }

        private void create_pointslist(List<PointLatLngAlt> pointslist, float _heading, double _lat, double _lng)
        {
            double distance = 0.02 / (6371); //km
            float angle = Math.Abs(_heading-360) * (float)Math.PI / 180 + (float)Math.PI; 
            double latend = 0;
            double lngend = 0;
            double latradians = _lat * Math.PI / 180;
            double lngradians = _lng * Math.PI / 180; 
            for (int i = 0; i < 2; i++)
            {
                latend = Math.Asin(Math.Sin(latradians) * Math.Cos(distance) + Math.Cos(latradians) * Math.Sin(distance) * Math.Cos(angle));
                if (Math.Cos(latradians) == 0)
                    lngend = lngradians;      // endpoint a pole
                else
                    lngend = mod(lngradians - Math.Asin(Math.Sin(angle) * Math.Sin(distance) / Math.Cos(latend)) + Math.PI, 2 * Math.PI) - Math.PI;

                double newlatend = latend * 180 / Math.PI;
                double newlngend = lngend * 180 / Math.PI;

                pointslist.Add(new PointLatLngAlt(newlatend, newlngend, 0, (i).ToString()));

                angle -= (float)Math.PI;
                distance = 0.08 / 6371;
            }
        }

       
        List<PointLatLngAlt> getGEAltPath(List<PointLatLngAlt> list)
        {
            double alt = 0;
            double lat = 0;
            double lng = 0;

            int pos = 0;

            List<PointLatLngAlt> answer = new List<PointLatLngAlt>();

            //http://code.google.com/apis/maps/documentation/elevation/
            //http://maps.google.com/maps/api/elevation/xml
            string coords = "";

            foreach (PointLatLngAlt loc in list)
            {
                if (loc == null)
                    continue;

                coords = coords + loc.Lat.ToString(new System.Globalization.CultureInfo("en-US")) + "," +
                         loc.Lng.ToString(new System.Globalization.CultureInfo("en-US")) + "|";
            }
            coords = coords.Remove(coords.Length - 1);

            if (list.Count < 2 || coords.Length > (2048 - 256))
            {
                //CustomMessageBox.Show("Too many/few WP's or to Big a Distance " + (distance / 1000) + "km", Strings.ERROR);
                return answer;
            }

            try
            {
                using (
                    XmlTextReader xmlreader =
                        new XmlTextReader("http://maps.google.com/maps/api/elevation/xml?path=" + coords + "&samples=" +
                                          (distance / 100).ToString(new System.Globalization.CultureInfo("en-US")) +
                                          "&sensor=false"))
                {
                    while (xmlreader.Read())
                    {
                        xmlreader.MoveToElement();
                        switch (xmlreader.Name)
                        {
                            case "elevation":
                                alt = double.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                Console.WriteLine("DO it " + lat + " " + lng + " " + alt);
                                PointLatLngAlt loc = new PointLatLngAlt(lat, lng, alt, "");
                                answer.Add(loc);
                                pos++;
                                break;
                            case "lat":
                                lat = double.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                break;
                            case "lng":
                                lng = double.Parse(xmlreader.ReadString(), new System.Globalization.CultureInfo("en-US"));
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            catch
            {
                //CustomMessageBox.Show("Error getting GE data", Strings.ERROR);
            }

            return answer;
        }

      
        List<PointLatLngAlt> getSRTMAltPath(List<PointLatLngAlt> list)
        {
            List<PointLatLngAlt> answer = new List<PointLatLngAlt>();

            PointLatLngAlt last = null;

            double disttotal = 0;

            foreach (PointLatLngAlt loc in list)
            {
                if (loc == null)
                    continue;

                if (last == null)
                {
                    last = loc;
                    continue;
                }

                double dist = last.GetDistance(loc);

                int points = (int)(dist / 10) + 1;

                double deltalat = (last.Lat - loc.Lat);
                double deltalng = (last.Lng - loc.Lng);

                double steplat = deltalat / points;
                double steplng = deltalng / points;

                PointLatLngAlt lastpnt = last;

                for (int a = 0; a <= points; a++)
                {
                    double lat = last.Lat - steplat * a;
                    double lng = last.Lng - steplng * a;

                    var newpoint = new PointLatLngAlt(lat, lng, srtm.getAltitude(lat, lng).alt, "");

                    double subdist = lastpnt.GetDistance(newpoint);

                    disttotal += subdist;

                    // srtm alts
                    //list3.Add(disttotal, newpoint.Alt);
                    answer.Add(newpoint);

                    lastpnt = newpoint;
                }

                //answer.Add(new PointLatLngAlt(loc.Lat, loc.Lng, srtm.getAltitude(loc.Lat, loc.Lng).alt, ""));

                last = loc;
            }

            return answer;
        }
    }

}