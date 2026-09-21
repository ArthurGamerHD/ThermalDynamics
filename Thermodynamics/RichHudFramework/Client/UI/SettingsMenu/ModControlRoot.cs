using System;
using System.Collections.Generic;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlContainerMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember,
		MyTuple<object, Func<int>>, // Member List
		object // ID
	>;

	namespace UI.Client
	{
		public sealed partial class RichHudTerminal
		{
			private class ModControlRoot : TerminalPageCategoryBase, IModControlRoot
			{
				public IReadOnlyList<TerminalPageCategoryBase> Subcategories { get; }

				public event EventHandler SelectionChanged;

/// <summary>ModControlRoot operation.</summary>
				public ModControlRoot(ControlContainerMembers data) : base(data)
				{
					GetOrSetMemberFunc(new Action(ModRootCallback), (int)ModControlRootAccessors.GetOrSetCallback);

/// <summary>Returns the orsetmemberfunc.</summary>
					var GetCategoryDataFunc = GetOrSetMemberFunc(null, (int)ModControlRootAccessors.GetCategoryAccessors)
						as Func<int, ControlContainerMembers>;

					Func<int, TerminalPageCategoryBase> GetPageFunc = (x => new TerminalPageCategoryWrapper(GetCategoryDataFunc(x)));
/// <summary>ReadOnlyApiCollection operation.</summary>
					Subcategories = new ReadOnlyApiCollection<TerminalPageCategoryBase>(GetPageFunc, data.Item2.Item2);
				}

/// <summary>ModRootCallback operation.</summary>
				protected void ModRootCallback()
				{
					SelectionChanged?.Invoke(this, EventArgs.Empty);
				}

/// <summary>Adds a .</summary>
				public void Add(TerminalPageCategoryBase subcategory) =>
					GetOrSetMemberFunc(subcategory.ID, (int)ModControlRootAccessors.AddSubcategory);

/// <summary>Adds a range.</summary>
				public void AddRange(IReadOnlyList<IModRootMember> members)
				{
					foreach (IModRootMember member in members)
					{
						if (member is TerminalPageBase)
							GetOrSetMemberFunc(member.ID, (int)TerminalPageCategoryAccessors.AddPage);
						else
							GetOrSetMemberFunc(member.ID, (int)ModControlRootAccessors.AddSubcategory);
					}
				}

				private class TerminalPageCategoryWrapper : TerminalPageCategoryBase
				{
/// <summary>TerminalPageCategoryWrapper operation.</summary>
					public TerminalPageCategoryWrapper(ControlContainerMembers data) : base(data)
					{ }
				}
			}
		}
	}
}