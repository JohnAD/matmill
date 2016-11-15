using System;
using System.Collections;
using System.Collections.Generic;

using CamBam.Geom;

namespace Matmill
{
    class Slice
    {        
        private Circle2F _ball;
        private double _max_engagement;
        private List<Arc2F> _segments = new List<Arc2F>();
        private Slice _prev_slice;
        private bool _is_undefined = true;

        public Circle2F Ball { get { return _ball; } }
        public Arc2F Arc { get { return _segments.Count == 1 ? _segments[0] : new Arc2F(_segments[0].P1, _segments[_segments.Count - 1].P2, _segments[0].Center, _segments[0].Direction); } }
        public bool Is_undefined { get { return _is_undefined; } }

        public Point2F Center { get { return _ball.Center; } }
        public double Radius { get { return _ball.Radius; } }
        public Slice Prev { get { return _prev_slice; } }
        public double Max_engagement { get { return _max_engagement; } }
        public List<Arc2F> Segments { get { return _segments; } }

        static private double angle_between_vectors(Vector2F v0, Vector2F v1, RotationDirection dir)
        {
            double angle = Math.Atan2(Vector2F.Determinant(v0, v1), Vector2F.DotProduct(v0, v1));
            if (angle < 0)
                angle += 2.0 * Math.PI;
            return (dir == RotationDirection.CCW) ? angle : (2.0 * Math.PI - angle);
        }

        static public double Calc_max_engagement(Point2F center, double radius, Slice prev_slice)
        {
            double delta_s = Point2F.Distance(center, prev_slice.Center);
            double delta_r = radius - prev_slice.Radius;
            return delta_s + delta_r;
        }

        // temporary lightwidth slice
        public Slice(Slice prev_slice, Point2F center, double radius, RotationDirection dir)
        {
            _prev_slice = prev_slice;
            _ball = new Circle2F(center, radius);
            _max_engagement = Slice.Calc_max_engagement(center, radius, _prev_slice);

            Line2F insects = _prev_slice.Ball.CircleIntersect(_ball);

            if (insects.p1.IsUndefined || insects.p2.IsUndefined)
                return;

            Arc2F arc = new Arc2F(_ball.Center, insects.p1, insects.p2, dir);

            if (!arc.VectorInsideArc(new Vector2F(_prev_slice.Center, _ball.Center)))
                arc = new Arc2F(_ball.Center, insects.p2, insects.p1, dir);

            _segments.Add(arc);
            _is_undefined = false;
        }

        public void Refine(List<Slice> colliding_slices, double end_clearance)
        {
            double clearance = end_clearance;

            // check if arc is small. refining is worthless in this case
            // criterion for smallness: there should be at least 4 segments with chord = clearance, plus
            // one segment to space ends far enough. A pentagon with a 5 segments with edge length = clearance
            // will define the min radius of circumscribed circle. clearance = 2 * R * sin (Pi / 5),
            // R = clearance / 2 / sin (Pi / 5)

            // XXX: assert if segments count != 1
            Arc2F arc = _segments[0];

            double r_min = clearance / 2 / Math.Sin(Math.PI / 5.0);
            if (arc.Radius <= r_min)
                return;

            if (colliding_slices.Contains(this))
            {
                // XXX: assert here
                Host.err("contains ME !");
                return;
            }

            // now apply the colliding slices. to keep things simple and robust, we apply just one slice - the one who trims
            // us most (removed length of arc is greatest).

            // end clearance adjustment:
            // to guarantee the cutter will never hit the unmilled area while rapiding between segments,
            // arc will always have original ends, trimming will happen in the middle only.
            // to prevent the cutter from milling extra small end segments and minimize numeric errors at small tangents,
            // original ends would always stick at least for a clearance (chordal) length.
            // i.e. if the point of intersection of arc and colliding circle is closer than clearance to the end,
            // it is moved to clearance distance.

            // there is a two cases of intersecting circles: with single intersection and with a double intersection.
            // double intersections splits arc to three pieces (length of the middle part is the measure),
            // single intesections splits arc in two (the part inside the circle is removed, its length is the measure).
            // in both cases the intersection points are subject to "end clearance adjustment".
            // single intersections are transformed to the double intersections, second point being one of the end clearances.

            // TODO: calculate clearance point the right way, with math :-)
            Line2F c1_insects = arc.CircleIntersect(new Circle2F(arc.P1, clearance));
            Line2F c2_insects = arc.CircleIntersect(new Circle2F(arc.P2, clearance));
            Point2F c1 = c1_insects.p1.IsUndefined ? c1_insects.p2 : c1_insects.p1;
            Point2F c2 = c2_insects.p1.IsUndefined ? c2_insects.p2 : c2_insects.p1;

            Line2F max_secant = new Line2F();
            double max_sweep = 0;

            foreach (Slice s in colliding_slices)
            {
                if (s == _prev_slice)
                    continue;  // no reason to process it
                Line2F secant = arc.CircleIntersect(s.Ball);

                if (secant.p1.IsUndefined && secant.p2.IsUndefined)
                    continue;

                if (secant.p1.IsUndefined || secant.p2.IsUndefined)
                {
                    // single intersection
                    Point2F splitpt = secant.p1.IsUndefined ? secant.p2 : secant.p1;
                    if (arc.P1.DistanceTo(s.Ball.Center) < arc.P2.DistanceTo(s.Ball.Center))
                    {
                        if (splitpt.DistanceTo(arc.P1) < clearance)
                            continue;  // nothing to remove
                        else if (splitpt.DistanceTo(arc.P2) < clearance)
                            secant = new Line2F(c1, c2);
                        else
                            secant = new Line2F(c1, splitpt);
                    }
                    else
                    {
                        // remove second segment
                        if (splitpt.DistanceTo(arc.P2) < clearance)
                            continue;
                        else if (splitpt.DistanceTo(arc.P1) < clearance)
                            secant = new Line2F(c1, c2);
                        else
                            secant = new Line2F(splitpt, c2);
                    }
                }
                else
                {
                    // double intersection
                    if (secant.p1.DistanceTo(arc.P1) < clearance)
                        secant.p1 = c1;
                    else if (secant.p1.DistanceTo(arc.P2) < clearance)
                        secant.p1 = c2;

                    if (secant.p2.DistanceTo(arc.P1) < clearance)
                        secant.p2 = c1;
                    else if (secant.p2.DistanceTo(arc.P2) < clearance)
                        secant.p2 = c2;
                }

                if (secant.p1.DistanceTo(secant.p2) < clearance * 2) // segment is too short, ignore it
                    continue;

                // sort insects by sweep (already sorted for single, may be unsorted for the double)
                Vector2F v_p1 = new Vector2F(arc.Center, arc.P1);
                Vector2F v_ins1 = new Vector2F(arc.Center, secant.p1);
                Vector2F v_ins2 = new Vector2F(arc.Center, secant.p2);

                double sweep = angle_between_vectors(v_ins1, v_ins2, arc.Direction);

                if (angle_between_vectors(v_p1, v_ins1, arc.Direction) > angle_between_vectors(v_p1, v_ins2, arc.Direction))
                {
                    secant = new Line2F(secant.p2, secant.p1);
                    sweep = 2.0 * Math.PI - sweep;
                }

                if (sweep > max_sweep)
                {
                    // ok, a last check - removed arc midpoint should be inside the colliding circle
                    Arc2F check_arc = new Arc2F(arc.Center, secant.p1, secant.p2, arc.Direction);
                    if (check_arc.Midpoint.DistanceTo(s.Ball.Center) < s.Ball.Radius)
                    {
                        max_sweep = sweep;
                        max_secant = secant;
                    }
                }
            }

            if (max_sweep == 0)
                return;

            Arc2F start = new Arc2F(arc.Center, arc.P1, max_secant.p1, arc.Direction);
            Arc2F removed = new Arc2F(arc.Center, max_secant.p1, max_secant.p2, arc.Direction);
            Arc2F end = new Arc2F(arc.Center, max_secant.p2, arc.P2, arc.Direction);

            _segments.Clear();
            _segments.Add(start);
            _segments.Add(end);

            // recalculate engagement if base engagement is no longer valid (midpoint vanished with the removed middle segment).
            // this engagement is 'virtual' and averaged with base to reduce stress on cutter abruptly entering the wall
            // TODO: is it really needed ?
            // TOD: should we apply opposite derating if base engagement is valid ?
            if (removed.VectorInsideArc(new Vector2F(arc.Center, arc.Midpoint)))
            {
                double e0 = _prev_slice.Center.DistanceTo(max_secant.p1) - _prev_slice.Radius;
                double e1 = _prev_slice.Center.DistanceTo(max_secant.p2) - _prev_slice.Radius;

                if (true)
                {
                    _max_engagement += Math.Max(e0, e1);
                    _max_engagement /= 2.0;
                }
                else
                {
                    _max_engagement = Math.Max(e0, e1);
                }
            }
        }

        public Slice(Point2F center, double radius, RotationDirection dir)
        {
            _ball = new Circle2F(center, radius);
            _max_engagement = 0;
            // XXX: hack, just for now
            //_arc = new Arc2F(center, radius, 0, 359);
            Arc2F arc0 = new Arc2F(center, radius, 0, 120);
            Arc2F arc1 = new Arc2F(center, radius, 120, 120);
            Arc2F arc2 = new Arc2F(center, radius, 240, 120);
            _segments.Add(arc0);
            _segments.Add(arc1);
            _segments.Add(arc2);
            _is_undefined = false;
        }
    }
}