using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
	[ProtoContract]
	public class Settings
	{
		public const string Filename = "ThermodynamicsConfig.cfg";
		public const string Name = "Thermodynamics";


		public const bool DebugTextureColors = true;

		public static Settings Instance;

        public static readonly MyStringHash DefaultSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamics");
        public static readonly MyStringHash DefaultLoopSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamicsLoop");

        [ProtoMember(1)]
		public int Version;

		[ProtoMember(2)]
        public bool DebugTextOnScreen;

		[ProtoMember(3)]
        public bool DebugTemperatureBlockColors;

        [ProtoMember(4)]
        public bool DebugSolarRadiationBlockColors;

        [ProtoMember(5)]
		public bool DebugSolarRaycast;

        [ProtoMember(6)]
        public bool DebugExposedSurfaceBlockColors;

		[ProtoMember(7)]
		public bool DebugWindRaycast;

        [ProtoMember(8)]
		public bool EnableEnvironment;

		[ProtoMember(9)]
		public bool EnableSolarHeat;

		[ProtoMember(10)]
		public bool EnablePlanets;


		[ProtoMember(14)]
		public bool EnableDamage;

		/// <summary>
		/// Aerodynamic heating at speed in atmosphere.
		/// </summary>
		[ProtoMember(11)]
		public bool EnableFriction;

		/// <summary>
		/// Coolant loop heat transport.
		/// </summary>
		[ProtoMember(12)]
		public bool EnableCoolantLoops;

		/// <summary>
		/// Limit each conduction exchange to the energy that equalises the pair. Keeps the
		/// solver bounded at low <see cref="Frequency"/>; turning it off reproduces the
		/// unbounded behaviour of the original.
		/// </summary>
		[ProtoMember(13)]
		public bool ClampConductionOvershoot;

		/// <summary>
		/// Apply overheat damage per second rather than per solver step. Per step makes damage
		/// scale with <see cref="Frequency"/>, which is what the original did.
		/// </summary>
		[ProtoMember(18)]
		public bool DamageIsPerSecond;

		/// <summary>
		/// Coefficient on the v^3 aerodynamic heating term.
		/// </summary>
		[ProtoMember(51)]
		public float FrictionScale;

		/// <summary>
		/// Simulation frames between solar occlusion raycasts. The sun moves slowly and a
		/// raycast is the most expensive thing a grid does, so it is worth reusing.
		/// </summary>
		[ProtoMember(52)]
		public int SolarOcclusionInterval;

        /// <summary>
        /// the number of update cycles per second
        /// </summary>
        [ProtoMember(15)]
		public int Frequency;

		/// <summary>
		/// the desired sim speed
		/// this will increase the frequency without changing the TimeScale
		/// </summary>
		[ProtoMember(16)]
		public float SimulationSpeed;

		/// <summary>
		/// How many times faster than real physics heat moves.
		///
		/// SpecificHeat in the block definitions is real J/(kg K), so a ship left alone behaves
		/// like a real one and takes hours to cool. This is the single number that trades that
		/// for a playable pace: it divides every heat capacity, which is exactly running thermal
		/// time faster. Equilibrium temperatures and the balance between conduction, radiation
		/// and coolant are unchanged — only the clock moves.
		///
		/// 1 is fully physical. 225 is the pace this mod shipped with: steel's real 450 J/(kg K)
		/// divided by 225 is the flat "2" the definitions used to carry.
		/// </summary>
		[ProtoMember(17)]
		public float HeatTimeScale;

		/// <summary>
		/// the temperature in kelven for space
		/// </summary>
		[ProtoMember(30)]
		public float VacuumTemperature;

		/// <summary>
		/// SolarEnergy = watts/m^2
		/// </summary>
		[ProtoMember(40)]
		public float SolarEnergy;

		[ProtoMember(50)]
		public float FrictionAtSpeedsAbove;

		[ProtoMember(60)]
		public bool DebugFrictionColors;

		/// <summary>
		/// Collect simulation, structure, environment and cost data for the whole session and
		/// write a report to world storage when the world closes.
		/// </summary>
		[ProtoMember(70)]
		public bool EnableTelemetry;

		/// <summary>
		/// One cell update in this many feeds the detailed per-block-type statistics. Peak
		/// temperatures, update counts and damage are always recorded. 1 samples everything.
		/// </summary>
		[ProtoMember(71)]
		public int TelemetrySampleStride;


        /// <summary>
        /// Used to adjust values that are calculated in seconds, to the current time scale 
        /// </summary>
        [XmlIgnore]
		public float TimeScaleRatio;

		[XmlIgnore]
		public float PerSecond;

		public static Settings GetDefaults()
		{
			Settings s = new Settings {
				Version = 3,
				DebugTextOnScreen = true,
				DebugTemperatureBlockColors = true,
				DebugSolarRadiationBlockColors = false,
				DebugExposedSurfaceBlockColors = false,
				DebugFrictionColors = false,
				DebugSolarRaycast = true,
				DebugWindRaycast = true,
				EnableEnvironment = true,
				EnableSolarHeat = true,
				EnablePlanets = true,
				EnableDamage = true,
				EnableFriction = true,
				EnableCoolantLoops = true,
				ClampConductionOvershoot = true,
				DamageIsPerSecond = true,
				Frequency = 4,
				SimulationSpeed = 1,
				HeatTimeScale = 225f,
				VacuumTemperature = 2.7f,
                SolarEnergy = 1000f,
				FrictionAtSpeedsAbove = 50f,
				FrictionScale = 0.001f,
				SolarOcclusionInterval = 12,
				EnableTelemetry = false,
				TelemetrySampleStride = 4,
            };

			s.Init();
			return s;
		}

		private void Init() {

			if (Frequency < 1)
				Frequency = 1;

			if (SimulationSpeed <= 0f)
				SimulationSpeed = 1f;

			if (HeatTimeScale <= 0f)
				HeatTimeScale = 1f;

			if (TelemetrySampleStride < 1)
				TelemetrySampleStride = 1;

			if (SolarOcclusionInterval < 1)
				SolarOcclusionInterval = 1;

			TimeScaleRatio =  1f/Frequency;
			PerSecond = Frequency * SimulationSpeed;

			_core = null;
		}

		[XmlIgnore]
		private Core.ThermalSettings _core;

		/// <summary>
		/// The same configuration in the form the simulation consumes. Built once and cached,
		/// because every grid holds a reference to it and the solver reads it every step.
		/// </summary>
		public Core.ThermalSettings ToCore()
		{
			if (_core != null) return _core;

			Core.ThermalSettings core = new Core.ThermalSettings();
			core.EnableEnvironment = EnableEnvironment;
			core.EnableSolarHeat = EnableSolarHeat;
			core.EnablePlanets = EnablePlanets;
			core.EnableFriction = EnableFriction;
			core.EnableDamage = EnableDamage;
			core.EnableCoolantLoops = EnableCoolantLoops;
			core.ClampConductionOvershoot = ClampConductionOvershoot;
			core.DamageIsPerSecond = DamageIsPerSecond;
			core.Frequency = Frequency;
			core.SimulationSpeed = SimulationSpeed;
			core.HeatTimeScale = HeatTimeScale;
			core.VacuumTemperature = VacuumTemperature;
			core.SolarEnergy = SolarEnergy;
			core.FrictionAtSpeedsAbove = FrictionAtSpeedsAbove;
			core.FrictionScale = FrictionScale;

			_core = core.Derive();
			return _core;
		}

		public static Settings Load()
		{
			Settings defaults = GetDefaults();
			Settings settings = defaults;
			try
			{
				if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
				{
					MyLog.Default.Info($"[{Name}] Loading saved settings");
					TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
					string text = reader.ReadToEnd();
					reader.Close();

					settings = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);

					if (settings.Version != defaults.Version)
					{
						MyLog.Default.Info($"[{Name}] Old version updating config {settings.Version}->{GetDefaults().Version}");
						settings = GetDefaults();
						Save(settings);
					}
				}
				else
				{
					MyLog.Default.Info($"[{Name}] Config file not found. Loading default settings");
					Save(settings);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[{Name}] Failed to load saved configuration. Loading defaults\n {e.ToString()}");
				Save(settings);
			}

			settings.Init();
			return settings;
		}

		public static void Save(Settings settings)
		{
			try
			{
				MyLog.Default.Info($"[{Name}] Saving Settings");
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
				writer.Close();
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[{Name}] Failed to save settings\n{e.ToString()}");
			}
		}
	}
}
