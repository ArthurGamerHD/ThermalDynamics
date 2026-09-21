using System.Collections.Generic;

namespace RichHudFramework
{
    namespace UI
    {
        using Server;
        using Client;

		public enum ControlPageAccessors : int
        {
            AddCategory = 10,

            CategoryData = 11,
        }

		public interface IControlPage : IControlPage<ControlCategory, ControlTile>
        { }

		public interface IControlPage<TCategory, TMember> : ITerminalPage, IEnumerable<TCategory>
/// <summary>new operation.</summary>
            where TCategory : IControlCategory<TMember>, new()
        {
            IReadOnlyList<TCategory> Categories { get; }

            IControlPage<TCategory, TMember> CategoryContainer { get; }

/// <summary>Adds a .</summary>
            void Add(TCategory category);
        }
    }
}