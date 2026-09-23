using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework.UI
{
	using CollectionData = MyTuple<Func<int, ApiMemberAccessor>, Func<int>>;
	using RichStringMembers = MyTuple<StringBuilder, GlyphFormatMembers>;

	public class ListBoxData<T> : ReadOnlyApiCollection<EntryData<T>>
	{
		public EntryData<T> Selection
		{
			get
			{
				var index = (int)GetOrSetMemberFunc(null, (int)ListBoxAccessors.SelectionIndex);
				return (index != -1) ? this[index] : null;
			}
		}

		public int SelectionIndex
		{
			get
			{
				return (int)GetOrSetMemberFunc(null, (int)ListBoxAccessors.SelectionIndex);
			}
		}

		private readonly ApiMemberAccessor GetOrSetMemberFunc;


		public ListBoxData(ApiMemberAccessor GetOrSetMemberFunc) : base(GetListData(GetOrSetMemberFunc))
		{
			this.GetOrSetMemberFunc = GetOrSetMemberFunc;
		}


		private static MyTuple<Func<int, EntryData<T>>, Func<int>> GetListData(ApiMemberAccessor GetOrSetMemberFunc)
		{
			var listData = (CollectionData)GetOrSetMemberFunc(null, (int)ListBoxAccessors.ListMembers);

			Func<int, EntryData<T>> GetEntryFunc = x => new EntryData<T>(listData.Item1(x));

			return new MyTuple<Func<int, EntryData<T>>, Func<int>>()
			{
				Item1 = GetEntryFunc,
				Item2 = listData.Item2
			};
		}


		public void Add(RichText text, T assocObject)
		{
			var data = new MyTuple<List<RichStringMembers>, object>()
			{
				Item1 = text.apiData,
				Item2 = assocObject
			};

			GetOrSetMemberFunc(data, (int)ListBoxAccessors.Add);
		}


		public void Insert(int index, RichText text, T assocObject)
		{
			var data = new MyTuple<int, List<RichStringMembers>, object>()
			{
				Item1 = index,
				Item2 = text.apiData,
				Item3 = assocObject
			};

			GetOrSetMemberFunc(data, (int)ListBoxAccessors.Insert);
		}


		public bool Remove(EntryData<T> entry) =>
			(bool)GetOrSetMemberFunc(entry.ID, (int)ListBoxAccessors.Remove);


		public void RemoveAt(int index) =>
			GetOrSetMemberFunc(index, (int)ListBoxAccessors.RemoveAt);


		public void Clear() =>
			GetOrSetMemberFunc(null, (int)ListBoxAccessors.ClearEntries);


		public void SetSelection(EntryData<T> entry) =>
			GetOrSetMemberFunc(entry.ID, (int)ListBoxAccessors.Selection);


		public void SetSelection(T assocMember) =>
			GetOrSetMemberFunc(assocMember, (int)ListBoxAccessors.SetSelectionAtData);


		public void SetSelection(int index) =>
			GetOrSetMemberFunc(index, (int)ListBoxAccessors.SelectionIndex);
	}

	public class EntryData<T>
	{
		public RichText Text
		{

			get { return new RichText(GetOrSetMemberFunc(null, (int)ListBoxEntryAccessors.Name) as List<RichStringMembers>); }

			set { GetOrSetMemberFunc(value.apiData, (int)ListBoxEntryAccessors.Name); }
		}

		public bool Enabled
		{

			get { return (bool)GetOrSetMemberFunc(null, (int)ListBoxEntryAccessors.Enabled); }

			set { GetOrSetMemberFunc(value, (int)ListBoxEntryAccessors.Enabled); }
		}

		public T AssocObject
		{

			get { return (T)GetOrSetMemberFunc(null, (int)ListBoxEntryAccessors.AssocObject); }

			set { GetOrSetMemberFunc(value, (int)ListBoxEntryAccessors.AssocObject); }
		}


		public object ID => GetOrSetMemberFunc(null, (int)ListBoxEntryAccessors.ID);

		private readonly ApiMemberAccessor GetOrSetMemberFunc;


		public EntryData(ApiMemberAccessor GetOrSetMemberFunc)
		{
			this.GetOrSetMemberFunc = GetOrSetMemberFunc;
		}
	}
}