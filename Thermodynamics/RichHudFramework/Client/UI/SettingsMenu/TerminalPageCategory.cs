using System;
using System.Collections;
using System.Collections.Generic;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlContainerMembers = MyTuple<
		ApiMemberAccessor,
		MyTuple<object, Func<int>>,
		object
	>;
	using ControlMembers = MyTuple<
		ApiMemberAccessor,
		object
	>;

	namespace UI.Client
	{
		public class TerminalPageCategory : TerminalPageCategoryBase
		{

			public TerminalPageCategory() : base(RichHudTerminal.Instance.GetNewPageCategory())
			{ }
		}

		public abstract class TerminalPageCategoryBase : ITerminalPageCategory
		{
			public string Name
			{

				get { return GetOrSetMemberFunc(null, (int)TerminalPageCategoryAccessors.Name) as string; }

				set { GetOrSetMemberFunc(value, (int)TerminalPageCategoryAccessors.Name); }
			}

			public IReadOnlyList<TerminalPageBase> Pages { get; }

			public ITerminalPageCategory PageContainer => this;

			public object ID => data.Item3;

			public TerminalPageBase SelectedPage
			{
				get
				{

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

				get { return (bool)GetOrSetMemberFunc(null, (int)TerminalPageCategoryAccessors.Enabled); }

				set { GetOrSetMemberFunc(value, (int)TerminalPageCategoryAccessors.Enabled); }
			}

			protected ApiMemberAccessor GetOrSetMemberFunc => data.Item1;

			protected readonly ControlContainerMembers data;


			public TerminalPageCategoryBase(ControlContainerMembers data)
			{
				this.data = data;

				var GetPageDataFunc = data.Item2.Item1 as Func<int, ControlMembers>;
				Func<int, TerminalPageBase> GetPageFunc = (x => new TerminalPage(GetPageDataFunc(x)));

				Pages = new ReadOnlyApiCollection<TerminalPageBase>(GetPageFunc, data.Item2.Item2);
			}


			public void Add(TerminalPageBase page) =>
				GetOrSetMemberFunc(page.ID, (int)TerminalPageCategoryAccessors.AddPage);


			public void AddRange(IReadOnlyList<TerminalPageBase> pages)
			{
				foreach (TerminalPageBase page in pages)
					GetOrSetMemberFunc(page.ID, (int)TerminalPageCategoryAccessors.AddPage);
			}


			public ControlContainerMembers GetApiData() =>
				data;


			public IEnumerator<TerminalPageBase> GetEnumerator() =>
				Pages.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				Pages.GetEnumerator();

			protected class TerminalPage : TerminalPageBase
			{

				public TerminalPage(ControlMembers data) : base(data)
				{ }
			}
		}
	}
}