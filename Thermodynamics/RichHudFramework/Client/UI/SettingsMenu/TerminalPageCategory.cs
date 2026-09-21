using System;
using System.Collections;
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
	using ControlMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember
		object // ID
	>;

	namespace UI.Client
	{
		public class TerminalPageCategory : TerminalPageCategoryBase
		{
/// <summary>TerminalPageCategory operation.</summary>
			public TerminalPageCategory() : base(RichHudTerminal.Instance.GetNewPageCategory())
			{ }
		}

		public abstract class TerminalPageCategoryBase : ITerminalPageCategory
		{
			public string Name
			{
/// <summary>Returns the orsetmemberfunc.</summary>
				get { return GetOrSetMemberFunc(null, (int)TerminalPageCategoryAccessors.Name) as string; }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)TerminalPageCategoryAccessors.Name); }
			}

			public IReadOnlyList<TerminalPageBase> Pages { get; }

			public ITerminalPageCategory PageContainer => this;

			public object ID => data.Item3;

			public TerminalPageBase SelectedPage
			{
				get
				{
/// <summary>Returns the orsetmemberfunc.</summary>
					object id = GetOrSetMemberFunc(null, (int)TerminalPageCategoryAccessors.Selection);

					if (id != null)
					{
						for (int n = 0; n < Pages.Count; n++)
						{
							if (id == Pages[n].ID)
								return Pages[n];
						}
					}

					return null;
				}
			}

			public bool Enabled
			{
/// <summary>return operation.</summary>
				get { return (bool)GetOrSetMemberFunc(null, (int)TerminalPageCategoryAccessors.Enabled); }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)TerminalPageCategoryAccessors.Enabled); }
			}

			protected ApiMemberAccessor GetOrSetMemberFunc => data.Item1;

			protected readonly ControlContainerMembers data;

/// <summary>TerminalPageCategoryBase operation.</summary>
			public TerminalPageCategoryBase(ControlContainerMembers data)
			{
				this.data = data;

				var GetPageDataFunc = data.Item2.Item1 as Func<int, ControlMembers>;
				Func<int, TerminalPageBase> GetPageFunc = (x => new TerminalPage(GetPageDataFunc(x)));
/// <summary>ReadOnlyApiCollection operation.</summary>
				Pages = new ReadOnlyApiCollection<TerminalPageBase>(GetPageFunc, data.Item2.Item2);
			}

/// <summary>Adds a .</summary>
			public void Add(TerminalPageBase page) =>
				GetOrSetMemberFunc(page.ID, (int)TerminalPageCategoryAccessors.AddPage);

/// <summary>Adds a range.</summary>
			public void AddRange(IReadOnlyList<TerminalPageBase> pages)
			{
				foreach (TerminalPageBase page in pages)
					GetOrSetMemberFunc(page.ID, (int)TerminalPageCategoryAccessors.AddPage);
			}

/// <summary>Returns the apidata.</summary>
			public ControlContainerMembers GetApiData() =>
				data;

/// <summary>Returns the enumerator.</summary>
			public IEnumerator<TerminalPageBase> GetEnumerator() =>
				Pages.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				Pages.GetEnumerator();

			protected class TerminalPage : TerminalPageBase
			{
/// <summary>TerminalPage operation.</summary>
				public TerminalPage(ControlMembers data) : base(data)
				{ }
			}
		}
	}
}