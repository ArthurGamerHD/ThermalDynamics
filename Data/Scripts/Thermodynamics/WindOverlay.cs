using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The wind field, drawn: a local disc of arrows on the ground and a lattice over the whole globe,
    /// because the field has two scales and one drawing cannot hold both. Client side, and it calls
    /// the same field functions the solver does. See configuration.md, The wind map.
    /// </summary>
    public static class WindOverlay
    {
        public enum Mode
        {
            Off = 0,
            Local = 1,
            Planet = 2,
        }

        /// <summary>Number of views, including off. Read by the keybind, the config clamp and the menu.</summary>
        public const int ModeCount = (int)Mode.Planet + 1;

        /// <summary>Cycled by ctrl+shift+W, seeded from <see cref="Settings.DebugWindOverlay"/>.</summary>
        public static Mode Current;

        private static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("Square");

        // ---- local view ---------------------------------------------------------------------

        /// <summary>
        /// Metres the local field reaches from the player. Several turnovers of the field's own
        /// 900 m variation scale, which is what makes the variation legible as a pattern.
        /// </summary>
        private const double LocalRadius = 5000d;

        /// <summary>
        /// Metres between local arrows. Against the radius this is a disc of about thirteen hundred
        /// arrows — the block overlay draws an order of magnitude more geometry than that on an
        /// ordinary ship, so the cost that matters here is the terrain lookup per arrow rather than
        /// the drawing.
        /// </summary>
        private const double LocalSpacing = 250d;

        /// <summary>
        /// Metres each arrow floats above the ground under it. Enough to clear boulders and small
        /// terrain detail without reading as flying.
        /// </summary>
        private const double LocalSurfaceOffset = 10d;

        // ---- planet view --------------------------------------------------------------------

        /// <summary>Degrees of latitude between rows. Poles are skipped: there is no east there.</summary>
        private const double PlanetLatitudeStep = 7.5d;

        /// <summary>Degrees of longitude between arrows in a row.</summary>
        private const double PlanetLongitudeStep = 15d;

        /// <summary>
        /// How far above the planet's largest radius the arrows float, as a share of it. Above every
        /// peak, so no arrow is ever buried, and near enough the surface to read as being on it.
        /// </summary>
        private const double PlanetAltitudeShare = 0.02d;

        /// <summary>Arrow length as a share of the planet's radius, at the storm end of the ramp.</summary>
        private const double PlanetArrowShare = 0.03d;

        // ---- sampling -----------------------------------------------------------------------

        /// <summary>
        /// Frames between resamples. The field is steady in position and changes only with the
        /// weather, so a third of a second is far more often than it can move.
        /// </summary>
        private const int SampleInterval = 20;

        /// <summary>
        /// Frames between resamples of the local field, which is much longer because that lattice
        /// asks the planet for a surface point per arrow. It is anchored to the world rather than to
        /// the player as well, so walking does not resample it — only crossing into the next cell
        /// does, and nothing but the weather can change an answer in between.
        /// </summary>
        private const int LocalSampleInterval = 180;

        /// <summary>
        /// Arrow width as a share of its distance from the eye — roughly two and a half pixels at
        /// 1080p. A line drawn at a fixed width in metres is invisible at five kilometres and a slab
        /// at five metres; holding the width on screen instead is what lets one lattice span both.
        /// </summary>
        private const double ScreenThickness = 0.0035d;

        private struct Arrow
        {
            public Vector3D Position;

            /// <summary>Where the wind blows here, world space, unit length.</summary>
            public Vector3 Direction;

            /// <summary>Its speed, m/s.</summary>
            public float Speed;

            /// <summary>Speed as a share of what this point could reach in a storm, 0..1.</summary>
            public float Share;
        }

        /// <summary>Rebuilt on the sample interval, redrawn every frame from the same list.</summary>
        private static readonly List<Arrow> Arrows = new List<Arrow>();

        /// <summary>
        /// The local lattice under construction. It is filled a slice at a time and swapped in whole,
        /// so the view never shows half a field, and the arrows already on screen stay there while
        /// the next set is being sampled.
        /// </summary>
        private static readonly List<Arrow> Building = new List<Arrow>();

        /// <summary>Lattice cells visited per frame while a local build is in flight.</summary>
        private const int LocalCellsPerFrame = 192;

        /// <summary>How far through the lattice the build has got, as a linear cell index.</summary>
        private static int buildCell = -1;

        /// <summary>Cells across the lattice being built, so the linear index can be decoded.</summary>
        private static int buildSide;

        // The frame this build was started with. Held rather than re-read so that every arrow in one
        // lattice is sampled against the same anchor and the same weather, however many frames it
        // takes to finish.
        private static Vector3D buildAnchor;
        private static Vector3D buildCentre;
        private static Vector3 buildEast;
        private static Vector3 buildNorth;
        private static Vector3 buildAxis;
        private static float buildWeather;
        private static float buildWeatherWind;

        private static int sinceSample = int.MaxValue;

        /// <summary>
        /// The world point the local lattice is laid out from, snapped to a whole number of
        /// <see cref="LocalSpacing"/> steps. Snapping is what stops the arrows sliding along under
        /// the player as they walk: the lattice belongs to the ground, so it must not move with the
        /// person looking at it.
        /// </summary>
        private static Vector3D localAnchor;
        private static long lastPlanetId;
        private static Mode lastMode;

        /// <summary>Length of the arrows in the current lattice, m. Set when it is sampled.</summary>
        private static double arrowLength;

        /// <summary>Line thickness for the current lattice, m.</summary>
        private static float arrowThickness;

        // ---- the wind where the player is ---------------------------------------------------

        /// <summary>
        /// The wind at the player, world space, whether or not anything is being drawn. Published
        /// for the HUD indicator, which wants a wind on foot as much as in a cockpit and has no
        /// grid to read one from when the player is walking.
        ///
        /// Zero when the player is not in air that moves.
        /// </summary>
        public static Vector3 PlayerWind { get; private set; }

        /// <summary>Away from the planet's centre at the player. Zero when there is no planet.</summary>
        public static Vector3 PlayerUp { get; private set; }

        /// <summary>Frames between samples of the player's own wind, which the HUD reads.</summary>
        private const int PlayerSampleInterval = 10;

        private static int sincePlayerSample = int.MaxValue;

        public static void Cycle()
        {
            Current = (Mode)(((int)Current + 1) % ModeCount);
            Announce();
        }

        public static void Set(Mode mode)
        {
            Current = mode;
            Announce();
        }

        private static void Announce()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            MyAPIGateway.Utilities.ShowNotification("wind map: " + Describe(Current), 2000, "White");
        }

        public static string Describe(Mode mode)
        {
            switch (mode)
            {
                case Mode.Local: return "local";
                case Mode.Planet: return "planet";
                default: return "off";
            }
        }

        public static void Reset()
        {
            Arrows.Clear();
            Building.Clear();
            buildCell = -1;
            sinceSample = int.MaxValue;
            sincePlayerSample = int.MaxValue;
            PlayerWind = Vector3.Zero;
            PlayerUp = Vector3.Zero;
        }

        public static void Draw()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

            MatrixD camera = MyAPIGateway.Session.Camera.WorldMatrix;
            Vector3D eye = camera.Translation;

            SamplePlayerWind(ref eye);

            if (Current == Mode.Off)
            {
                if (Arrows.Count > 0) Arrows.Clear();
                if (buildCell >= 0) AbandonBuild();
                return;
            }

            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(eye);
            if (planet == null || planet.Entity == null)
            {
                Arrows.Clear();
                AbandonBuild();
                return;
            }

            if (DueToSample(planet, ref eye)) Rebuild(planet, ref eye);
            if (buildCell >= 0) StepLocalBuild(planet);

            for (int i = 0; i < Arrows.Count; i++)
            {
                Arrow arrow = Arrows[i];

                // The far side of a planet, drawn through it. Nothing occludes transparent geometry
                // here, so without this the globe view is twice as many arrows as it should be and
                // half of them are running backwards.
                if (Current == Mode.Planet)
                {
                    Vector3D toEye = eye - arrow.Position;
                    Vector3D up = arrow.Position - planet.Entity.PositionComp.GetPosition();
                    if (Vector3D.Dot(toEye, up) <= 0d) continue;
                }

                DrawArrow(ref arrow, ref eye);
            }
        }

        /// <summary>
        /// Whether the lattice needs resampling: the view changed, the player walked out of it, or
        /// enough frames passed that the weather could have moved.
        /// </summary>
        private static bool DueToSample(PlanetManager.Planet planet, ref Vector3D eye)
        {
            if (Current != lastMode || planet.Entity.EntityId != lastPlanetId) return true;

            // A build already in flight is left to finish. Without this the emptiness of the very
            // first lattice asks for a rebuild on the next frame, which restarts the slice — and a
            // view that restarts its build every frame never finishes one and draws nothing at all.
            if (buildCell >= 0) return false;

            if (Arrows.Count == 0) return true;

            sinceSample++;
            if (sinceSample >= (Current == Mode.Local ? LocalSampleInterval : SampleInterval))
                return true;

            // The globe lattice is fixed to the planet and never follows anyone. The local one is
            // laid out from a snapped anchor, so it moves a whole cell at a time and only when the
            // player has actually crossed into the next one.
            return Current == Mode.Local && Anchor(ref eye) != localAnchor;
        }

        /// <summary>The lattice origin for a player at this point: their position, snapped.</summary>
        private static Vector3D Anchor(ref Vector3D eye)
        {
            return new Vector3D(
                Math.Round(eye.X / LocalSpacing) * LocalSpacing,
                Math.Round(eye.Y / LocalSpacing) * LocalSpacing,
                Math.Round(eye.Z / LocalSpacing) * LocalSpacing);
        }

        private static void Rebuild(PlanetManager.Planet planet, ref Vector3D eye)
        {
            // A view that is merely resampling keeps what it is showing until the replacement is
            // ready. One that has just been switched to must not: the globe lattice left over from
            // the last view would otherwise hang in the air for the few frames the local build takes,
            // drawn at the wrong scale and with its far side no longer culled.
            bool switched = Current != lastMode || planet.Entity.EntityId != lastPlanetId;

            sinceSample = 0;
            localAnchor = Anchor(ref eye);
            lastMode = Current;
            lastPlanetId = planet.Entity.EntityId;

            if (switched) Arrows.Clear();

            // Weather is sampled once, at the player, and applied to every arrow. Asking per arrow
            // means a string allocation and a lookup for each of several hundred points every
            // resample, which is not worth paying for a debug view — and on the globe lattice it
            // would be sampling one storm's worth of weather at points thousands of kilometres from
            // it in any case. The consequence is honest and worth knowing: **the map draws the
            // weather you are standing in, everywhere.**
            float weather;
            float weatherWind;
            SampleWeather(ref eye, out weather, out weatherWind);

            if (Current == Mode.Local)
            {
                // Started rather than done. The arrows already up stay up until the new lattice is
                // complete, so a resample is invisible rather than a blink.
                StartLocalBuild(planet, weather, weatherWind);
                return;
            }

            Arrows.Clear();
            BuildPlanet(planet, weather, weatherWind);
        }

        private static void SampleWeather(ref Vector3D position, out float weather, out float weatherWind)
        {
            weather = 0f;
            weatherWind = 1f;

            float influence = Settings.Instance.ClimateWeatherInfluence;
            if (influence <= 0f) return;

            weather = MyVisualScriptLogicProvider.GetWeatherIntensity(position);
            if (weather <= 0f)
            {
                weather = 0f;
                return;
            }

            string name = MyVisualScriptLogicProvider.GetWeather(position);
            if (string.IsNullOrEmpty(name)) return;

            weatherWind = WeatherResponse.Soften(
                WeatherResponse.Soften(WeatherResponse.For(name), influence), weather).WindMultiplier;
        }

        /// <summary>
        /// Begins the local lattice: a disc of arrows five kilometres across, laid out from a snapped
        /// anchor so it belongs to the landscape rather than to the player.
        /// </summary>
        private static void StartLocalBuild(
            PlanetManager.Planet planet, float weather, float weatherWind)
        {
            buildCell = -1;

            buildCentre = planet.Entity.PositionComp.GetPosition();
            buildAnchor = localAnchor;

            Vector3D offset = buildAnchor - buildCentre;
            if (offset.LengthSquared() <= 0d) return;

            Vector3 up = (Vector3)Vector3D.Normalize(offset);
            buildAxis = planet.Entity.PositionComp.WorldMatrixRef.Up;

            Vector3 east = Vector3.Cross(buildAxis, up);
            if (east.LengthSquared() < 1e-6f)
            {
                // Directly over a pole, where there is no east. Any tangent will do for laying the
                // lattice out; the wind at each point is still sampled from its own position.
                east = Vector3.Cross(up, planet.Entity.PositionComp.WorldMatrixRef.Forward);
                if (east.LengthSquared() < 1e-6f) return;
            }

            buildEast = Vector3.Normalize(east);
            buildNorth = Vector3.Normalize(Vector3.Cross(up, buildEast));

            buildWeather = weather;
            buildWeatherWind = weatherWind;

            buildSide = (2 * (int)(LocalRadius / LocalSpacing)) + 1;
            buildCell = 0;

            Building.Clear();
        }

        private static void AbandonBuild()
        {
            buildCell = -1;
            Building.Clear();
        }

        /// <summary>
        /// One slice of the local lattice. The terrain lookup per arrow is the one call here that is
        /// not cheap and there are about thirteen hundred of them, so the build is spread over frames.
        /// A disc rather than a square, whose corners reach half again as far as its edges.
        /// See configuration.md, The wind map.
        /// </summary>
        private static void StepLocalBuild(PlanetManager.Planet planet)
        {
            if (Current != Mode.Local || planet.Entity == null)
            {
                AbandonBuild();
                return;
            }

            int half = buildSide / 2;
            double radiusSquared = LocalRadius * LocalRadius;
            int total = buildSide * buildSide;

            for (int done = 0; done < LocalCellsPerFrame && buildCell < total; done++, buildCell++)
            {
                int i = (buildCell / buildSide) - half;
                int j = (buildCell % buildSide) - half;

                double across = i * LocalSpacing;
                double along = j * LocalSpacing;

                // Outside the disc. Skipped without costing a terrain lookup, but still counted
                // against the slice, so a frame's work is bounded by cells visited rather than by
                // arrows produced.
                if ((across * across) + (along * along) > radiusSquared) continue;

                Vector3D flat = buildAnchor
                    + ((Vector3D)buildEast * across) + ((Vector3D)buildNorth * along);

                // Onto the terrain, then clear of it. The tangent plane sags away from a sphere over
                // five kilometres, so this is also what stops the far edge of the lattice being
                // buried by the planet's own curvature.
                Vector3D surface = planet.Entity.GetClosestSurfacePointGlobal(ref flat);

                Vector3D lift = surface - buildCentre;
                if (lift.LengthSquared() <= 0d) continue;

                Vector3D position = surface + (Vector3D.Normalize(lift) * LocalSurfaceOffset);

                Add(Building, planet, ref position, ref buildCentre, buildAxis,
                    buildWeather, buildWeatherWind);
            }

            if (buildCell < total) return;

            // Complete. Swapped in whole rather than appended to, so the view never holds arrows
            // from two lattices at once.
            Arrows.Clear();
            Arrows.AddRange(Building);
            Building.Clear();
            buildCell = -1;

            arrowLength = LocalSpacing * 0.8d;
            arrowThickness = (float)(arrowLength * 0.012d);
        }

        /// <summary>A latitude and longitude lattice over the whole globe.</summary>
        private static void BuildPlanet(PlanetManager.Planet planet, float weather, float weatherWind)
        {
            Vector3D centre = planet.Entity.PositionComp.GetPosition();
            MatrixD matrix = planet.Entity.PositionComp.WorldMatrixRef;

            Vector3 axis = matrix.Up;
            Vector3D poleAxis = Vector3D.Normalize(matrix.Up);
            Vector3D prime = Vector3D.Normalize(matrix.Forward);
            Vector3D side = Vector3D.Normalize(Vector3D.Cross(poleAxis, prime));

            double radius = planet.Entity.MaximumRadius * (1d + PlanetAltitudeShare);

            arrowLength = planet.Entity.MaximumRadius * PlanetArrowShare;
            arrowThickness = (float)(arrowLength * 0.06d);

            // The poles themselves are skipped rather than drawn: the circulation has no east there,
            // so the field returns no direction and an arrow would be a lie about which way it
            // points rather than an absence.
            for (double latitude = -90d + PlanetLatitudeStep;
                latitude <= 90d - PlanetLatitudeStep + 1e-9d;
                latitude += PlanetLatitudeStep)
            {
                double lat = latitude * Math.PI / 180d;
                double ringRadius = Math.Cos(lat);
                double height = Math.Sin(lat);

                // Constant angular spacing would crowd the poles with arrows nobody can tell apart.
                // The step is widened by the shrinking ring so the spacing on the ground stays even.
                double step = ringRadius > 1e-3d
                    ? Math.Min(120d, PlanetLongitudeStep / ringRadius)
                    : 120d;

                for (double longitude = 0d; longitude < 360d - 1e-9d; longitude += step)
                {
                    double lon = longitude * Math.PI / 180d;

                    Vector3D up = (poleAxis * height)
                        + (prime * (ringRadius * Math.Cos(lon)))
                        + (side * (ringRadius * Math.Sin(lon)));

                    Vector3D position = centre + (Vector3D.Normalize(up) * radius);

                    Add(Arrows, planet, ref position, ref centre, axis, weather, weatherWind);
                }
            }
        }

        /// <summary>
        /// Samples the field at a point and keeps the arrow, if there is a wind there to draw.
        ///
        /// This is the same pair of calls the solver's own wind sample makes, in the same order and
        /// with the same ceiling, so the map cannot drift away from what the grids are flying
        /// through without the solver drifting with it.
        /// </summary>
        private static void Add(
            List<Arrow> into, PlanetManager.Planet planet, ref Vector3D position,
            ref Vector3D centre, Vector3 axis, float weather, float weatherWind)
        {
            Vector3 up = (Vector3)Vector3D.Normalize(position - centre);

            Vector3 direction = WindField.Direction(up, axis);
            if (direction.LengthSquared() < 1e-6f) return;

            float ceiling = planet.Entity.GetWindSpeed(position);
            if (ceiling <= 0f) return;

            float speed = WindField.Speed(
                ceiling, weather, WindField.Variation(position), weatherWind);
            if (speed <= 0f) return;

            Arrow arrow = new Arrow();
            arrow.Position = position;
            arrow.Direction = direction;
            arrow.Speed = speed;

            // Against the storm end of the ramp rather than against the ceiling: the ceiling is the
            // planet's maximum and calm air is a tenth of it, so scaling against it would draw every
            // ordinary day as a field of stubs. This puts a still day near zero and the worst
            // weather the game reports near one.
            arrow.Share = Clamp01(speed / (ceiling * WindField.StormFraction));

            into.Add(arrow);
        }

        /// <summary>
        /// The wind where the player is, kept up to date whether or not the map is being drawn.
        ///
        /// The HUD needs an answer on foot, where there is no grid holding a sampled environment,
        /// and it needs the same answer in a cockpit so that stepping out of a ship does not change
        /// the reading. One sample, taken here, serves both.
        /// </summary>
        private static void SamplePlayerWind(ref Vector3D eye)
        {
            sincePlayerSample++;
            if (sincePlayerSample < PlayerSampleInterval) return;
            sincePlayerSample = 0;

            PlayerWind = Vector3.Zero;
            PlayerUp = Vector3.Zero;

            PlanetManager.Planet planet = PlanetManager.GetClosestPlanet(eye);
            if (planet == null || planet.Entity == null || !planet.Entity.HasAtmosphere) return;

            Vector3D centre = planet.Entity.PositionComp.GetPosition();
            Vector3D offset = eye - centre;
            if (offset.LengthSquared() <= 0d) return;

            Vector3 up = (Vector3)Vector3D.Normalize(offset);
            PlayerUp = up;

            float ceiling = planet.Entity.GetWindSpeed(eye);
            if (ceiling <= 0f) return;

            Vector3 direction = WindField.Direction(up, planet.Entity.PositionComp.WorldMatrixRef.Up);
            if (direction.LengthSquared() < 1e-6f) return;

            float weather;
            float weatherWind;
            SampleWeather(ref eye, out weather, out weatherWind);

            PlayerWind = direction
                * WindField.Speed(ceiling, weather, WindField.Variation(eye), weatherWind);
        }

        /// <summary>
        /// One arrow: a shaft along the wind with two strokes swept back from its tip, all three
        /// lying in the plane the wind blows along so the head reads from above and from the side.
        /// </summary>
        private static void DrawArrow(ref Arrow arrow, ref Vector3D eye)
        {
            Vector3D direction = (Vector3D)arrow.Direction;

            // Short arrows for slow air, so speed is visible in the shape as well as the colour —
            // a colour ramp alone is unreadable on a field of hundreds. Never shorter than a third,
            // or a calm region turns into a field of dots with no direction in it.
            double length = arrowLength * (0.35d + (0.65d * arrow.Share));

            Vector3D tail = arrow.Position - (direction * (length * 0.5d));
            Vector3D tip = arrow.Position + (direction * (length * 0.5d));

            Vector4 colour = Colour(arrow.Share).ToVector4();

            // Width is held on the screen rather than in the world, because one lattice spans two
            // orders of magnitude of distance. Floored so a near arrow is not a thread, capped at the
            // arrow's own length so a far one shrinks away rather than swelling into a blob.
            double thickness = Math.Max(
                arrowThickness, Vector3D.Distance(eye, arrow.Position) * ScreenThickness);
            if (thickness > length * 0.15d) thickness = length * 0.15d;

            MySimpleObjectDraw.DrawLine(tail, tip, LineMaterial, ref colour, (float)thickness);

            // The head is swept in the plane containing the wind and the line of sight, which keeps
            // it facing the camera whatever angle the arrow is seen from. A head in the ground plane
            // disappears edge-on the moment you look along the wind.
            Vector3D toEye = eye - arrow.Position;
            Vector3D sweep = Vector3D.Cross(direction, toEye);

            if (sweep.LengthSquared() < 1e-12d) return;
            sweep = Vector3D.Normalize(sweep);

            double head = length * 0.3d;
            Vector3D back = tip - (direction * head);

            MySimpleObjectDraw.DrawLine(
                tip, back + (sweep * head * 0.5d), LineMaterial, ref colour, (float)thickness);
            MySimpleObjectDraw.DrawLine(
                tip, back - (sweep * head * 0.5d), LineMaterial, ref colour, (float)thickness);
        }

        /// <summary>
        /// Calm to storm as blue through green and yellow to red. A ramp of its own rather than the
        /// temperature one, which spends most of its range on colours a wind map has no use for.
        /// </summary>
        public static Color Colour(float share)
        {
            share = Clamp01(share);

            if (share < 0.5f) return Lerp(new Color(60, 130, 235), new Color(90, 220, 120), share * 2f);
            return Lerp(new Color(90, 220, 120), new Color(235, 70, 55), (share - 0.5f) * 2f);
        }

        private static Color Lerp(Color from, Color to, float amount)
        {
            return new Color(
                (int)(from.R + ((to.R - from.R) * amount)),
                (int)(from.G + ((to.G - from.G) * amount)),
                (int)(from.B + ((to.B - from.B) * amount)));
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
