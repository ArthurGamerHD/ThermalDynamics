using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using Client;

	namespace UI.Client
	{
		using BindClientMembers = MyTuple<
			ApiMemberAccessor,
			MyTuple<Func<int, object, int, object>, Func<int>>,
			MyTuple<Func<Vector2I, object, int, object>, Func<int, int>>,
			Func<Vector2I, int, bool>,
			MyTuple<Func<int, int, object>, Func<int>>,
			Action
		>;

		public sealed partial class BindManager : RichHudClient.ApiModule
		{
			public const int MaxBindLength = 3;

			public static IReadOnlyList<IBindGroup> Groups => Instance.groups;

			public static IReadOnlyList<IControl> Controls => Instance.controls;

			public static SeBlacklistModes BlacklistMode
			{
				get
				{
					if (_instance == null) Init();
					return (SeBlacklistModes)_instance.GetOrSetMemberFunc(null, (int)BindClientAccessors.RequestBlacklistMode);
				}
				set
				{
					if (_instance == null)
						Init();

					lastBlacklist = value;
					_instance.GetOrSetMemberFunc(value, (int)BindClientAccessors.RequestBlacklistMode);
				}
			}

			public static bool IsChatOpen 
			{
				get 
				{
                    if (_instance == null) Init();
                    return (bool)_instance?.GetOrSetMemberFunc(null, (int)BindClientAccessors.IsChatOpen);
                }
			}


			private static BindManager Instance
			{

				get { Init(); return _instance; }
			}
			private static BindManager _instance;

			private readonly Func<int, object, int, object> GetOrSetGroupMemberFunc;
			private readonly Func<int> GetGroupCountFunc;

			private readonly Func<Vector2I, object, int, object> GetOrSetBindMemberFunc;
			private readonly Func<Vector2I, int, bool> IsBindPressedFunc;
			private readonly Func<int, int> GetBindCountFunc;

			private readonly Func<int, int, object> GetControlMember;
			private readonly Func<int> GetControlCountFunc;

			private readonly ApiMemberAccessor GetOrSetMemberFunc;
			private readonly Action UnloadAction;

			private readonly ReadOnlyApiCollection<IBindGroup> groups;
			private readonly ReadOnlyApiCollection<IControl> controls;
			private readonly List<int> conIDbuf;
			private readonly List<List<int>> aliasIDbuf;

			private static SeBlacklistModes lastBlacklist, tmpBlacklist;


			private BindManager() : base(ApiModuleTypes.BindManager, false, true)
			{
				var clientData = (BindClientMembers)GetApiData();

				GetOrSetMemberFunc = clientData.Item1;
				UnloadAction = clientData.Item6;

				GetOrSetGroupMemberFunc = clientData.Item2.Item1;
				GetGroupCountFunc = clientData.Item2.Item2;

				IsBindPressedFunc = clientData.Item4;
				GetOrSetBindMemberFunc = clientData.Item3.Item1;
				GetBindCountFunc = clientData.Item3.Item2;

				GetControlMember = clientData.Item5.Item1;
				GetControlCountFunc = clientData.Item5.Item2;


				groups = new ReadOnlyApiCollection<IBindGroup>(x => new BindGroup(x), GetGroupCountFunc);

				controls = new ReadOnlyApiCollection<IControl>(x => new Control(x), GetControlCountFunc);


				conIDbuf = new List<int>();
				aliasIDbuf = new List<List<int>>();
			}


			private static void Init()
			{
				if (_instance == null)
				{

					_instance = new BindManager();
				}
			}


			public override void Close()
			{
				UnloadAction?.Invoke();
				_instance = null;
			}


			public static void RequestTempBlacklist(SeBlacklistModes mode)
			{
				tmpBlacklist |= mode;
			}


			public override void Draw()
			{
				GetOrSetMemberFunc(lastBlacklist | tmpBlacklist, (int)BindClientAccessors.RequestBlacklistMode);
				tmpBlacklist = SeBlacklistModes.None;
			}


			public static IBindGroup GetOrCreateGroup(string name)
			{
				var index = (int)Instance.GetOrSetMemberFunc(name, (int)BindClientAccessors.GetOrCreateGroup);
				return index != -1 ? Groups[index] : null;
			}


			public static IBindGroup GetBindGroup(string name)
			{
				var index = (int)Instance.GetOrSetMemberFunc(name, (int)BindClientAccessors.GetBindGroup);
				return index != -1 ? Groups[index] : null;
			}


			public static ControlHandle GetControl(string name)
			{
				var index = (int)Instance.GetOrSetMemberFunc(name, (int)BindClientAccessors.GetControlByName);
				return new ControlHandle(index);
			}


			public static string GetControlName(ControlHandle con)
			{
				return Instance.GetOrSetMemberFunc(con.id, (int)BindClientAccessors.GetControlName) as string;
			}


			public static string GetControlName(int conID)
			{
				return Instance.GetOrSetMemberFunc(conID, (int)BindClientAccessors.GetControlName) as string;
			}


			public static string[] GetControlNames(IReadOnlyList<int> conIDs)
			{
				return Instance.GetOrSetMemberFunc(conIDs, (int)BindClientAccessors.GetControlNames) as string[];
			}


			public static IControl GetControl(ControlHandle handle) =>
				Controls[handle.id];


			public static void GetComboIndices(IReadOnlyList<ControlHandle> controls, List<int> combo, bool sanitize = true)
			{
				combo.Clear();

				for (int n = 0; n < controls.Count; n++)
					combo.Add(controls[n].id);

				if (sanitize)
					SanitizeCombo(combo);
			}


			private static IReadOnlyList<int> GetComboIndicesTemp(IReadOnlyList<ControlHandle> controls, bool sanitize = true)
			{
				var buf = _instance.conIDbuf;
				GetComboIndices(controls, buf, sanitize);
				return buf;
			}


			private static void SanitizeCombo(List<int> combo)
			{
				combo.Sort();

				for (int i = combo.Count - 1; i > 0; i--)
				{
					if (combo[i] == combo[i - 1] || combo[i] <= 0)
						combo.RemoveAt(i);
				}

				if (combo.Count > 0 && combo[0] == 0)
					combo.RemoveAt(0);
			}
		}
	}
}