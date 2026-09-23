using System;
using System.Collections.Generic;

namespace RichHudFramework
{
    namespace UI
    {
        public interface IBind
        {
            string Name { get; }

            int Index { get; }

            int AliasCount { get; }

            bool Analog { get; }

            float AnalogValue { get; }

            bool IsNewPressed { get; }

            bool IsPressed { get; }

            bool IsPressedAndHeld { get; }

            bool IsReleased { get; }

            event EventHandler NewPressed;

            event EventHandler PressedAndHeld;

            event EventHandler Released;


            List<ControlHandle> GetCombo(int alias = 0);


            List<int> GetConIDs(int alias = 0);


            bool TrySetCombo(IReadOnlyList<ControlHandle> combo, int alias = 0, bool isStrict = true, bool isSilent = true);


            bool TrySetCombo(IReadOnlyList<int> combo, int alias = 0, bool isStrict = true, bool isSilent = true);


            bool TrySetCombo(IReadOnlyList<string> combo, int alias = 0, bool isStrict = true, bool isSilent = true);


            void ClearCombo(int alias = 0);


            void ClearSubscribers();
        }

        public enum BindAccesssors : int
        {
            Name = 1,

            Analog = 2,

            Index = 3,

            IsPressed = 4,

            IsNewPressed = 5,

            IsPressedAndHeld = 6,

            IsReleased = 7,

            OnNewPress = 8,

            OnPressAndHold = 9,

            OnRelease = 10,

            GetCombo = 11,

            TrySetComboWithIndices = 12,

            TrySetComboWithNames = 13,

            ClearCombo = 14,

            ClearSubscribers = 15,

            AnalogValue = 16,

            AliasCount = 17,

            ToString = 18,
        }

    }
}