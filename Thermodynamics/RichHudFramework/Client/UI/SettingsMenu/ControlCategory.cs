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

	namespace UI.Client
	{
		public class ControlCategory : IControlCategory
		{
			public string HeaderText
			{
/// <summary>Returns the orsetmemberfunc.</summary>
				get { return GetOrSetMemberFunc(null, (int)ControlCatAccessors.HeaderText) as string; }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)ControlCatAccessors.HeaderText); }
			}

			public string SubheaderText
			{
/// <summary>Returns the orsetmemberfunc.</summary>
				get { return GetOrSetMemberFunc(null, (int)ControlCatAccessors.SubheaderText) as string; }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)ControlCatAccessors.SubheaderText); }
			}

			public IReadOnlyList<ControlTile> Tiles { get; }

			public IControlCategory TileContainer => this;

			public object ID => data.Item3;

			public bool Enabled
			{
/// <summary>return operation.</summary>
				get { return (bool)GetOrSetMemberFunc(null, (int)ControlCatAccessors.Enabled); }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)ControlCatAccessors.Enabled); }
			}

			private ApiMemberAccessor GetOrSetMemberFunc => data.Item1;
			private readonly ControlContainerMembers data;

/// <summary>ControlCategory operation.</summary>
			public ControlCategory() : this(RichHudTerminal.Instance.GetNewMenuCategory())
			{ }

/// <summary>ControlCategory operation.</summary>
			public ControlCategory(ControlContainerMembers data)
			{
				this.data = RichHudTerminal.Instance.GetNewMenuCategory();

				var GetTileDataFunc = data.Item2.Item1 as Func<int, ControlContainerMembers>;
/// <summary>ControlTile operation.</summary>
				Func<int, ControlTile> GetTileFunc = x => new ControlTile(GetTileDataFunc(x));

/// <summary>ReadOnlyApiCollection operation.</summary>
				Tiles = new ReadOnlyApiCollection<ControlTile>(GetTileFunc, data.Item2.Item2);
			}

			IEnumerator<ControlTile> IEnumerable<ControlTile>.GetEnumerator() =>
				Tiles.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				Tiles.GetEnumerator();

/// <summary>Adds a .</summary>
			public void Add(ControlTile tile) =>
				GetOrSetMemberFunc(tile.ID, (int)ControlCatAccessors.AddMember);

/// <summary>Returns the apidata.</summary>
			public ControlContainerMembers GetApiData() =>
				data;
		}
	}
}