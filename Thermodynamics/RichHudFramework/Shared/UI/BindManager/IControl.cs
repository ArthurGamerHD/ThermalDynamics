namespace RichHudFramework
{
    namespace UI
    {
        using Client;
        using Server;

		public interface IControl
        {
            string Name { get; }

            string DisplayName { get; }

            int Index { get; }

            bool IsPressed { get; }

            bool IsNewPressed { get; }

            bool IsReleased { get; }

            bool Analog { get; }

            float AnalogValue { get; }
        }

        public enum ControlAccessors : int
        {
            Name = 1,

            DisplayName = 2,

            Index = 3,

            IsPressed = 4,

            Analog = 5,

            IsNewPressed = 6,

            IsReleased = 7,

            AnalogValue = 8
        }
    }
}