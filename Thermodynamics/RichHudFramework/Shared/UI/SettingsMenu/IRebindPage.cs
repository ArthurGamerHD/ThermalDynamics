using System.Collections.Generic;

namespace RichHudFramework
{
	namespace UI
	{
		public enum RebindPageAccessors : int
		{
			Add = 10,
		}

		public interface IRebindPage : ITerminalPage, IEnumerable<IBindGroup>
		{
			IReadOnlyList<IBindGroup> BindGroups { get; }

/// <summary>Adds a .</summary>
			void Add(IBindGroup bindGroup, bool isAliased = false);

/// <summary>Adds a .</summary>
			void Add(IBindGroup bindGroup, BindDefinition[] defaultBinds, bool isAliased = false);
		}
	}
}