using Draygo.BlockExtensionsAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Drawing;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PlanetManager : MySessionComponentBase
    {
        public static readonly double SunSize = 0.045f;
        public static readonly double Denominator = 1 - SunSize;
        public static readonly PlanetDefinition NullDef = new PlanetDefinition();

        public class Planet
        {
            public MyPlanet Entity;
            public Vector3D Position;
            public MyGravityProviderComponent GravityComponent;
            private PlanetDefinition definition = NullDef;

            /// <summary>
            /// This planet's thermal definition, or null until the definition lookup can answer.
            ///
            /// The lookup is a mod-to-mod API that initialises on a message, so the first grid to
            /// tick may ask before it exists. A null answer is kept rather than cached, and the
            /// caller asks again next time — the alternative is a whole session run against the
            /// blank definition of a planet that has one.
            ///
            /// Keyed by the *generator*, not the entity. A planet entity's own DefinitionId is
            /// <c>MyObjectBuilder_Planet/(null)</c> — a field dump's game log has it verbatim — so
            /// looking that up matched nothing and every planet fell through to the fallback. The
            /// generator's id is <c>PlanetGeneratorDefinition/EarthLike</c>, which is what
            /// Planets.xml keys its entries on.
            /// </summary>
            public PlanetDefinition Definition() 
            {
                if (definition == NullDef && Entity.Generator != null)
                {
                    PlanetDefinition read = PlanetDefinition.GetDefinition(Entity.Generator.Id);
                    if (read == null) return null;

                    definition = read;

                    MyLog.Default.Info($"[{Settings.Name}] updated planet definition: {Entity.DisplayName}"
                        + $" ({definition.Supplied})");
                }

                return definition == NullDef ? null : definition;
            }
        }

        public class ExternalForceData
        {
            public Vector3D Gravity = Vector3D.Zero;
            public Vector3D WindDirection = Vector3D.Zero;
            public float WindSpeed;
            public float AtmosphericPressure;
        }
 
        private static List<Planet> Planets = new List<Planet>();

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Entities.OnEntityAdd += AddPlanet;
            MyAPIGateway.Entities.OnEntityRemove += RemovePlanet;
        }

        private void AddPlanet(IMyEntity ent)
        {
            if (ent is MyPlanet)
            {
                MyPlanet entity = ent as MyPlanet;

                //MyLog.Default.Info($"[{Settings.Name}] Added Planet: {entity.DisplayName} - {entity.DefinitionId.HasValue}");
                Planets.Add(new Planet()
                {
                    Entity = entity,
                    Position = entity.PositionComp.WorldMatrixRef.Translation,
                    GravityComponent = entity.Components.Get<MyGravityProviderComponent>(),
                });
            }
        }

        private void RemovePlanet(IMyEntity ent)
        {
            Planets.RemoveAll(p => p.Entity.EntityId == ent.EntityId);
        }


        /// <summary>
        /// The gravity force vector applied at a location, and the total air pressure there.
        /// </summary>
        public static ExternalForceData GetExternalForces(Vector3D worldPosition)
        {
            ExternalForceData data = new ExternalForceData();

            Planet planet = null;
            double distance = double.MaxValue;
            foreach (Planet p in Planets)
            {
                data.Gravity += p.GravityComponent.GetWorldGravity(worldPosition);

                double d = (p.Position - worldPosition).LengthSquared();
                if (d < distance)
                {
                    planet = p;
                    distance = d;
                }
            }

            if (planet?.Entity.HasAtmosphere == true)
            {
                data.AtmosphericPressure = planet.Entity.GetAirDensity(worldPosition);
                data.WindSpeed = planet.Entity.GetWindSpeed(worldPosition);
            }

            return data;
        }

        /// <summary>The planet nearest a world position, or null when there is none.</summary>
        public static Planet GetClosestPlanet(Vector3D position) 
        {
            Planet current = null;
            double distance = double.MaxValue;
            for (int i = 0; i < Planets.Count; i++) 
            {
                Planet p = Planets[i];
                double d = (p.Position - position).LengthSquared();
                if (d < distance) 
                {
                    current = p;
                    distance = d;
                }
            }

            return current;
        }
    }
}
