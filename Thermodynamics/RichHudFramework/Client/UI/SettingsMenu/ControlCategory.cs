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

	namespace UI.Client
	{
		public class ControlCategory : IControlCategory
		{
			public string HeaderText
			{

				get { return GetOrSetMemberFunc(null, (int)ControlCatAccessors.HeaderText) as string; }

				set { GetOrSetMemberFunc(value, (int)ControlCatAccessors.HeaderText); }
			}

			public string SubheaderText
			{

				get { return GetOrSetMemberFunc(null, (int)ControlCatAccessors.SubheaderText) as string; }

				set { GetOrSetMemberFunc(value, (int)ControlCatAccessors.SubheaderText); }
			}

			public IReadOnlyList<ControlTile> Tiles { get; }

			public IControlCategory TileContainer => this;

			public object ID => data.Item3;

			public bool Enabled
			{

				get { return (bool)GetOrSetMemberFunc(null, (int)ControlCatAccessors.Enabled); }

				set { GetOrSetMemberFunc(value, (int)ControlCatAccessors.Enabled); }
			}

			private ApiMemberAccessor GetOrSetMemberFunc => data.Item1;
			private readonly ControlContainerMembers data;


			public ControlCategory() : this(RichHudTerminal.Instance.GetNewMenuCategory())
			{ }


			public ControlCategory(ControlContainerMembers data)
			{
				this.data = RichHudTerminal.Instance.GetNewMenuCategory();

				var GetTileDataFunc = data.Item2.Item1 as Func<int, ControlContainerMembers>;

				Func<int, ControlTile> GetTileFunc = x => new ControlTile(GetTileDataFunc(x));


				Tiles = new ReadOnlyApiCollection<ControlTile>(GetTileFunc, data.Item2.Item2);
			}

			IEnumerator<ControlTile> IEnumerable<ControlTile>.GetEnumerator() =>
				Tiles.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				Tiles.GetEnumerator();


			public void Add(ControlTile tile) =>
				GetOrSetMemberFunc(tile.ID, (int)ControlCatAccessors.AddMember);


			public ControlContainerMembers GetApiData() =>
				data;
		}
	}
}