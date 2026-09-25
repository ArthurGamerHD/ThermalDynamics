using System.Collections;
using System.Collections.Generic;
using VRage;

namespace RichHudFramework
{
	using BindDefinitionData = MyTuple<string, string[], string[][]>;

	namespace UI.Client
	{
		public class RebindPage : TerminalPageBase, IRebindPage
		{
			public IReadOnlyList<IBindGroup> BindGroups => bindGroups;

			public RebindPage GroupContainer => this;

			private readonly List<IBindGroup> bindGroups;


			public RebindPage() : base(ModPages.RebindPage)
			{

				bindGroups = new List<IBindGroup>();
			}


			public void Add(IBindGroup bindGroup, bool isAliased = false)
			{
				GetOrSetMemberFunc(new MyTuple<object, BindDefinitionData[], bool>(bindGroup.ID, null, isAliased), (int)RebindPageAccessors.Add);
				bindGroups.Add(bindGroup);
			}


            public void Add(IBindGroup bindGroup, BindDefinition[] defaultBinds, bool isAliased = false)
			{
				BindDefinitionData[] data = new BindDefinitionData[defaultBinds.Length];

				for (int n = 0; n < defaultBinds.Length; n++)
					data[n] = (BindDefinitionData)defaultBinds[n];

				GetOrSetMemberFunc(new MyTuple<object, BindDefinitionData[], bool>(bindGroup.ID, data, isAliased), (int)RebindPageAccessors.Add);
				bindGroups.Add(bindGroup);
			}


			public IEnumerator<IBindGroup> GetEnumerator() =>
				bindGroups.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				bindGroups.GetEnumerator();
		}
	}
}