namespace RichHudFramework
{
    namespace UI
    {
        public interface IReadOnlyHudNode : IReadOnlyHudParent
        {
            IReadOnlyHudParent Parent { get; }

			bool Registered { get; }
        }
    }
}