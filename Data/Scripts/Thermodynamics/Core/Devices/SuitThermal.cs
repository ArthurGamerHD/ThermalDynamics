using System;

namespace Thermodynamics.Core
{
    /// <summary>What one step does to the person inside the suit.</summary>
    public struct SuitStepResult
    {
        /// <summary>Suit interior temperature at the end of the step, K.</summary>
        public float InteriorKelvin;

        /// <summary>Watts the regulation moved. Negative is heat put back in.</summary>
        public float RegulatedWatts;

        /// <summary>Damage the occupant took over the step, in the game's own hit points.</summary>
        public float Damage;

        /// <summary>
        /// True when the load is more than the suit can shift, so the interior is following the
        /// environment rather than being held.
        ///
        /// **This is the state a warning is about**, and it arrives before the damage does: the
        /// interior has to cross the whole gap between comfort and the limit first, which is the
        /// few seconds a player has to get out.
        /// </summary>
        public bool Overwhelmed;
    }

    /// <summary>
    /// A suit as one lumped thermal mass with a cooler on it.
    ///
    /// <para>
    /// The occupant is held near body temperature by machinery, which is the assumption the whole
    /// model rests on: a suit is not insulation, it is a refrigerator that happens to be wearable.
    /// A hot room is therefore survivable while the cooler keeps up and lethal past the point where
    /// it does not — and *that* temperature is something a player learns by playing, rather than a
    /// threshold handed to them.
    /// </para>
    ///
    /// <para>
    /// **An open helmet is a second heat path, not a second rule.** Breathing puts the room
    /// against the lung surface, which no suit wall stands in the way of, so the conductance goes
    /// up by <see cref="OpenHelmetConductanceFactor"/> and the same cooler is asked to shift ten
    /// times as much. The consequence falls out rather than being authored: the survivable
    /// temperature drops from 510 K to 330 K, and 57 C is about where breathing hot air stops
    /// being merely unpleasant.
    /// </para>
    ///
    /// <para>
    /// **The cooler is free**, and that is a limit rather than a decision. `IMyCharacter` exposes
    /// `SuitEnergyLevel` to read and nothing to write, so a mod cannot charge a player for running
    /// it; what it can do is notice when the suit is flat, which is what
    /// <paramref name="energyAvailable"/> is. See backlog `C16`.
    /// </para>
    ///
    /// <para>
    /// Free of any Space Engineers type, and stateless but for the interior temperature the caller
    /// carries, so all of it is testable outside a session (`C5`).
    /// </para>
    /// </summary>
    public static class SuitThermal
    {
        /// <summary>
        /// The temperature the regulation aims at, K — 37 C, body temperature.
        ///
        /// A constant rather than a setting: it is a property of the occupant, and a world that
        /// wants a different suit has the conductance, the capacity and the cooling rating to say
        /// so with.
        /// </summary>
        public const float ComfortKelvin = 310.15f;

        /// <summary>
        /// How much better the environment reaches an occupant whose helmet is open.
        ///
        /// Breathing is a heat path a sealed suit does not have, and a fast one — air reaches the
        /// lung surface rather than crossing a suit wall. Ten times, which leaves a player in a
        /// burning compartment seconds and one in a warm compartment perfectly comfortable.
        /// </summary>
        public const float OpenHelmetConductanceFactor = 10f;

        /// <summary>
        /// One step of the suit. <paramref name="interiorKelvin"/> is where the last step left it,
        /// <paramref name="environmentKelvin"/> the air the occupant is in, and
        /// <paramref name="energyAvailable"/> whether the suit has charge to run the cooler on.
        /// </summary>
        public static SuitStepResult Step(ThermalSettings settings, float interiorKelvin,
            float environmentKelvin, bool helmetOpen, bool energyAvailable, float seconds)
        {
            SuitStepResult result = new SuitStepResult();
            result.InteriorKelvin = interiorKelvin;

            if (settings == null || seconds <= 0f) return result;

            // The switch is honoured here as well as at the call site. The call site is what makes
            // it free — it returns before it looks up a player — and this is what makes "off" mean
            // off wherever the model is reached from (`C7`).
            if (!settings.EnableSuitDamage) return result;

            // Divided by the clock exactly as every block's capacity is, so a player heats on the
            // same clock as the ship around them rather than 225 times slower than it.
            float scale = settings.HeatTimeScale > 0f ? settings.HeatTimeScale : 1f;
            float capacity = settings.SuitHeatCapacity / scale;
            if (capacity <= 0f) capacity = ThermalConstants.MinimumThermalMass;

            float conductance = settings.SuitConductance;
            if (helmetOpen) conductance *= OpenHelmetConductanceFactor;

            // What the environment is doing to the occupant, before anything resists it.
            float load = conductance * (environmentKelvin - interiorKelvin);

            // Regulation cancels the load and closes whatever gap is already open, so the suit
            // recovers rather than holding wherever it drifted to. Both directions, up to one
            // rating: a suit that could only cool would leave a player to freeze in shadow.
            //
            // An open helmet does not switch it off — the suit still covers the body, and what has
            // changed is the load it is being asked to carry.
            float regulated = 0f;

            if (energyAvailable && settings.SuitCoolingWatts > 0f)
            {
                float wanted = load + (capacity * (interiorKelvin - ComfortKelvin) / seconds);
                float limit = settings.SuitCoolingWatts;
                regulated = wanted > limit ? limit : (wanted < -limit ? -limit : wanted);
            }

            result.Overwhelmed = load > 0f && regulated < load;

            float updated = interiorKelvin + ((load - regulated) * seconds / capacity);
            if (updated < ThermalConstants.MinimumTemperature)
            {
                updated = ThermalConstants.MinimumTemperature;
            }

            result.InteriorKelvin = updated;
            result.RegulatedWatts = regulated;

            // The same shape a block is damaged with: per kelvin of overshoot, per second.
            float over = updated - settings.SuitCriticalTemperature;
            if (over > 0f) result.Damage = over * settings.SuitDamagePerKelvin * seconds;

            return result;
        }

        /// <summary>
        /// The hottest environment the suit can hold indefinitely, K: where its rating exactly
        /// cancels what leaks in at comfort temperature.
        ///
        /// The number a player is really learning, so it is computed rather than authored — a world
        /// that moves the conductance or the rating moves this with it.
        /// </summary>
        public static float SurvivableKelvin(ThermalSettings settings, bool helmetOpen = false)
        {
            if (settings == null || settings.SuitConductance <= 0f) return float.PositiveInfinity;

            float conductance = settings.SuitConductance;
            if (helmetOpen) conductance *= OpenHelmetConductanceFactor;

            return ComfortKelvin + (settings.SuitCoolingWatts / conductance);
        }
    }
}
