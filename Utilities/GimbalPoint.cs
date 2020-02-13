using System;
using System.Drawing;

namespace MissionPlanner.Utilities
{
    public class GimbalPoint
    {
        public static int Yawchannel = 7;
        public static int Pitchchannel = 5;
        public static int Rollchannel = -1;

        public enum Axis
        {
            roll,
            pitch,
            yaw
        }

        /// returns the angle (degrees*100) that the RC_Channel input is receiving
        static float Angle_input(bool Rev, float Radio_in, float Radio_min, float Radio_max, float Angle_min,
            float Angle_max)
        {
            return (Rev ? -1.0f : 1.0f)*(Radio_in - Radio_min)*(Angle_max - Angle_min)/(Radio_max - Radio_min) +
                   (Rev ? Angle_max : Angle_min);
        }

        static int Channelpwm(int Channel)
        {
            if (Channel == 1)
                return (int) (float) MainV2.comPort.MAV.cs.ch1out;
            if (Channel == 2)
                return (int) (float) MainV2.comPort.MAV.cs.ch2out;
            if (Channel == 3)
                return (int) (float) MainV2.comPort.MAV.cs.ch3out;
            if (Channel == 4)
                return (int) (float) MainV2.comPort.MAV.cs.ch4out;
            if (Channel == 5)
                return (int) (float) MainV2.comPort.MAV.cs.ch5out;
            if (Channel == 6)
                return (int) (float) MainV2.comPort.MAV.cs.ch6out;
            if (Channel == 7)
                return (int) (float) MainV2.comPort.MAV.cs.ch7out;
            if (Channel == 8)
                return (int) (float) MainV2.comPort.MAV.cs.ch8out;

            return 0;
        }

        public static double ConvertPwmtoAngle(Axis axis)
        {
            int Pwmvalue = -1;

            if (!MainV2.comPort.MAV.param.ContainsKey("RC" + Yawchannel + "_MIN"))
                return 0;

            switch (axis)
            {
                case GimbalPoint.Axis.roll:
                    Pwmvalue = Channelpwm(Rollchannel);
                    float Minr = (float) MainV2.comPort.MAV.param["RC" + Rollchannel + "_MIN"];
                    float Maxr = (float) MainV2.comPort.MAV.param["RC" + Rollchannel + "_MAX"];
                    float Minroll = (float) MainV2.comPort.MAV.param["MNT_ANGMIN_ROL"];
                    float Maxroll = (float) MainV2.comPort.MAV.param["MNT_ANGMAX_ROL"];
                    float Revr = (float) MainV2.comPort.MAV.param["RC" + Rollchannel + "_REV"];

                    return Angle_input(Revr != 1, Pwmvalue, Minr, Maxr, Minroll, Maxroll)/100.0;

                case GimbalPoint.Axis.pitch:
                    Pwmvalue = Channelpwm(Pitchchannel);
                    float Minp = (float) MainV2.comPort.MAV.param["RC" + Pitchchannel + "_MIN"];
                    float Maxp = (float) MainV2.comPort.MAV.param["RC" + Pitchchannel + "_MAX"];
                    float Minpitch = (float) MainV2.comPort.MAV.param["MNT_ANGMIN_TIL"];
                    float Maxpitch = (float) MainV2.comPort.MAV.param["MNT_ANGMAX_TIL"];
                    float Revp = (float) MainV2.comPort.MAV.param["RC" + Pitchchannel + "_REV"];


                    return Angle_input(Revp != 1, Pwmvalue, Minp, Maxp, Minpitch, Maxpitch)/100.0;

                case GimbalPoint.Axis.yaw:
                    Pwmvalue = Channelpwm(Yawchannel);
                    float Miny = (float) MainV2.comPort.MAV.param["RC" + Yawchannel + "_MIN"];
                    float Maxy = (float) MainV2.comPort.MAV.param["RC" + Yawchannel + "_MAX"];
                    float Minyaw = (float) MainV2.comPort.MAV.param["MNT_ANGMIN_PAN"];
                    float Maxyaw = (float) MainV2.comPort.MAV.param["MNT_ANGMAX_PAN"];
                    float Revy = (float) MainV2.comPort.MAV.param["RC" + Yawchannel + "_REV"];

                    return Angle_input(Revy != 1, Pwmvalue, Miny, Maxy, Minyaw, Maxyaw)/100.0;
            }

            return 0;
        }

        public static PointF FindLineIntersection(PointF start1, PointF end1, PointF start2, PointF end2)
        {
            double denom = ((end1.X - start1.X)*(end2.Y - start2.Y)) - ((end1.Y - start1.Y)*(end2.X - start2.X));
            //  AB & CD are parallel         
            if (denom == 0)
                return new PointF();
            double numer = ((start1.Y - start2.Y)*(end2.X - start2.X)) - ((start1.X - start2.X)*(end2.Y - start2.Y));
            double r = numer/denom;
            double numer2 = ((start1.Y - start2.Y)*(end1.X - start1.X)) - ((start1.X - start2.X)*(end1.Y - start1.Y));
            double s = numer2/denom;
            if ((r < 0 || r > 1) || (s < 0 || s > 1))
                return new PointF();

            // Find intersection point      
            PointF result = new PointF
            {
                X = (float)(start1.X + (r * (end1.X - start1.X))),
                Y = (float)(start1.Y + (r * (end1.Y - start1.Y)))
            };
            return result;
        }

        public static PointLatLngAlt ProjectPoint()
        {
            MainV2.comPort.GetMountStatus();

            // this should be looking at rc_channel function
            Yawchannel = (int) (float) MainV2.comPort.MAV.param["MNT_RC_IN_PAN"];

            Pitchchannel = (int) (float) MainV2.comPort.MAV.param["MNT_RC_IN_TILT"];

            Rollchannel = (int) (float) MainV2.comPort.MAV.param["MNT_RC_IN_ROLL"];

            //if (!MainV2.comPort.BaseStream.IsOpen)
            //  return PointLatLngAlt.Zero;

            PointLatLngAlt currentlocation = new PointLatLngAlt(MainV2.comPort.MAV.cs.lat, MainV2.comPort.MAV.cs.lng);

            double yawangle = MainV2.comPort.MAV.cs.campointc;
            double rollangle = MainV2.comPort.MAV.cs.campointb;
            double pitchangle = MainV2.comPort.MAV.cs.campointa;

            //
            if ((double) MainV2.comPort.MAV.param["MNT_TYPE"] == 4) //comment out whole section to only use calculated campoint.
            {
                //updated compoent ID from 67 to 154
                yawangle = MainV2.comPort.MAVlist[MainV2.comPort.sysidcurrent, 154].cs.yaw;
                rollangle = MainV2.comPort.MAVlist[MainV2.comPort.sysidcurrent, 154].cs.roll;
                pitchangle = MainV2.comPort.MAVlist[MainV2.comPort.sysidcurrent, 154].cs.pitch;
            }

            if (Math.Abs(rollangle) > 180 || yawangle == 0 && pitchangle == 0)
            {
                yawangle = ConvertPwmtoAngle(Axis.yaw);
                //rollangle = ConvertPwmtoAngle(axis.roll);
                pitchangle = ConvertPwmtoAngle(Axis.pitch) + MainV2.comPort.MAV.cs.pitch;

                pitchangle -= Math.Sin(yawangle*MathHelper.deg2rad)*MainV2.comPort.MAV.cs.roll;
            }

            // tan (o/a)
            // todo account for ground elevation change.

            int distout = 10;
            PointLatLngAlt newpos = PointLatLngAlt.Zero;

            //dist = Math.Tan((90 + pitchangle) * MathHelper.deg2rad) * (MainV2.comPort.MAV.cs.alt);

            while (distout < 1000)
            {
                // get a projected point to test intersection against - not using slope distance
                PointLatLngAlt newposdist = currentlocation.newpos(yawangle + MainV2.comPort.MAV.cs.yaw, distout);
                newposdist.Alt = srtm.getAltitude(newposdist.Lat, newposdist.Lng).alt;

                // get another point 50 infront
                PointLatLngAlt newposdist2 = currentlocation.newpos(yawangle + MainV2.comPort.MAV.cs.yaw, distout + 50);
                newposdist2.Alt = srtm.getAltitude(newposdist2.Lat, newposdist2.Lng).alt;

                // get the flat terrain distance out - at 0 alt
                double distflat = Math.Tan((90 + pitchangle)*MathHelper.deg2rad)*(MainV2.comPort.MAV.cs.altasl);

                // x is dist from plane, y is alt
                var newpoint = FindLineIntersection(new PointF(0, MainV2.comPort.MAV.cs.altasl),
                    new PointF((float) distflat, 0),
                    new PointF((float) distout, (float) newposdist.Alt),
                    new PointF((float) distout + 50, (float) newposdist2.Alt));

                if (newpoint.X != 0)
                {
                    newpos = newposdist2;
                    break;
                }

                distout += 50;
            }

            //Console.WriteLine("pitch " + pitchangle.ToString("0.000") + " yaw " + yawangle.ToString("0.000") + " dist" + dist.ToString("0.000"));

            //PointLatLngAlt newpos = currentlocation.newpos( yawangle + MainV2.comPort.MAV.cs.yaw, dist);

            //Console.WriteLine(newpos);
            return newpos;
        }
    }
}