using System.Collections.Generic;

namespace RichHudFramework
{
    namespace UI
    {
        using Server;
        using Client;

        public enum TerminalPageCategoryAccessors : int
        {
            Name = 2,

            Enabled = 3,

            Selection = 4,

            AddPage = 5,

            AddPageRange = 6,
        }

        public interface ITerminalPageCategory : IEnumerable<TerminalPageBase>, IModRootMember
        {
            IReadOnlyList<TerminalPageBase> Pages { get; }

            ITerminalPageCategory PageContainer { get; }

            TerminalPageBase SelectedPage { get; }

/// <summary>Adds a .</summary>
            void Add(TerminalPageBase page);

/// <summary>Adds a range.</summary>
            void AddRange(IReadOnlyList<TerminalPageBase> pages);
        }
    }
}