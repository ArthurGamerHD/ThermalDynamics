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
/// <summary>PlanetDefinition operation.</summary>
        public static readonly PlanetDefinition NullDef = new PlanetDefinition();

        public class Planet
        {
            public MyPlanet Entity;
            public Vector3D Position;
            public MyGravityProviderComponent GravityComponent;
            private PlanetDefinition definition = NullDef;

/// <summary>Definition operation.</summary>
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

/// <summary>List operation.</summary>
        private static List<Planet> Planets = new List<Planet>();

/// <summary>CopyPlanets operation.</summary>
        public static void CopyPlanets(List<Planet> target) { target.Clear(); target.AddRange(Planets); }

/// <summary>Init operation.</summary>
        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Entities.OnEntityAdd += AddPlanet;
            MyAPIGateway.Entities.OnEntityRemove += RemovePlanet;
        }

/// <summary>Adds a planet.</summary>
        private void AddPlanet(IMyEntity ent)
        {
            if (ent is MyPlanet)
            {
                MyPlanet entity = ent as MyPlanet;

                Planets.Add(new Planet()
                {
                    Entity = entity,
                    Position = entity.PositionComp.WorldMatrixRef.Translation,
                    GravityComponent = entity.Components.Get<MyGravityProviderComponent>(),
                });
            }
        }

/// <summary>Removes the planet.</summary>
        private void RemovePlanet(IMyEntity ent)
        {
            Planets.RemoveAll(p => p.Entity.EntityId == ent.EntityId);
        }


/// <summary>Returns the closestplanet.</summary>
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
