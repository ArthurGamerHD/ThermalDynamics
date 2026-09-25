using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using HudNodeHookData = VRage.MyTuple<
	System.Func<object, int, object>,
	System.Action,
	System.Action,
	System.Action,
	System.Action<bool>,
	System.Action
>;
using HudSpaceOriginFunc = System.Func<VRageMath.Vector3D>;

namespace RichHudFramework
{
	using HudNodeData = MyTuple<
		uint[],
		Func<Vector3D>[],
		HudNodeHookData,
		object,
		List<object>,
		object
	>;

	namespace UI
	{
		using Client;
		using Server;
		using Internal;
		using System.Reflection;
		using static RichHudFramework.UI.NodeConfigIndices;
		using HudNodeDataHandle = IReadOnlyList<HudNodeData>;

		public abstract partial class HudParentBase : IReadOnlyHudParent
		{
			public virtual IReadOnlyHudSpaceNode HudSpace { get; protected set; }

			public bool Visible
			{

				get { return (Config[StateID] & (uint)HudElementStates.IsVisible) > 0; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.IsVisible;
					else
						_config[StateID] &= ~(uint)HudElementStates.IsVisible;
				}
			}

			public bool InputEnabled
			{

				get { return (Config[StateID] & Config[InputMaskID]) == Config[InputMaskID]; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.IsInputEnabled;
					else
						_config[StateID] &= ~(uint)HudElementStates.IsInputEnabled;
				}
			}

			public sbyte ZOffset
			{

				get { return (sbyte)Config[ZOffsetID]; }
				set
				{
					bool isVisible = (Config[StateID] & Config[VisMaskID]) == Config[VisMaskID];

					if (isVisible && Config[ZOffsetID] != (uint)value)
					{
						uint[] rootConfig = HudMain.Instance._root._config;
						bool isActive = Math.Abs((int)Config[FrameNumberID] - (int)rootConfig[FrameNumberID]) < 2;

						if (isActive)
							rootConfig[StateID] |= (uint)HudElementStates.IsStructureStale;
					}

					_config[ZOffsetID] = (uint)value;
				}
			}

			#region INTERNAL DATA

			public HudNodeDataHandle DataHandle { get; }

			public IReadOnlyList<uint> Config { get; }

			protected readonly uint[] _config;

			protected readonly HudNodeData[] _dataHandle;

			protected readonly List<object> childHandles;

			protected readonly List<HudNodeBase> children;

			private struct HookUsages
			{
				public bool IsInputDepthCustom;
				public bool IsHandleInputCustom;
				public bool IsMeasureCustom;
				public bool IsLayoutCustom;
				public bool IsDrawCustom;
			}

			private sealed class HookCanary : HudParentBase
			{
				public static readonly bool IsInitialized;		

				public static readonly IReadOnlyDictionary<Type, HookUsages> TypeHookMap;

				public static readonly MemberInfo InputDepthBase;

				public static readonly MemberInfo HandleInputBase;
				
				public static readonly MemberInfo MeasureBase;

				public static readonly MemberInfo LayoutBase;

				public static readonly MemberInfo DrawBase;


				public static void AddType(HudParentBase node, Type objType)
				{

					var usages = default(HookUsages);

					{
						Action InputDepthAction = node.InputDepth;

						if (InputDepthAction.Method != InputDepthBase)
							usages.IsInputDepthCustom = true;
					}
					{
						Action<Vector2> HandleInputAction = node.HandleInput;

						if (HandleInputAction.Method != HandleInputBase)
							usages.IsHandleInputCustom = true;
					}
					{
						Action MeasureAction = node.Measure;

						if (MeasureAction.Method != MeasureBase)
							usages.IsMeasureCustom = true;
					}
					{
						Action LayoutAction = node.Layout;

						if (LayoutAction.Method != LayoutBase)
							usages.IsLayoutCustom = true;
					}
					{
						Action DrawAction = node.Draw;

						if (DrawAction.Method != DrawBase)
							usages.IsDrawCustom = true;
					}

					_typeHookMap.Add(objType, usages);
				}

				private static readonly Dictionary<Type, HookUsages> _typeHookMap;


				static HookCanary()
				{

					var temp = new HookCanary();

					InputDepthBase = ((Action)temp.InputDepth).Method;
					HandleInputBase = ((Action<Vector2>)temp.HandleInput).Method;
					MeasureBase = ((Action)temp.Measure).Method;
					LayoutBase = ((Action)temp.Layout).Method;
					DrawBase = ((Action)temp.Draw).Method;

					_typeHookMap = new Dictionary<Type, HookUsages>();
					TypeHookMap = _typeHookMap;

					IsInitialized = true;
				}


				private HookCanary() { }
			}

			#endregion


			public HudParentBase()
			{
				if (HookCanary.IsInitialized)
				{

					children = new List<HudNodeBase>();

					childHandles = new List<object>();
					_config = new uint[ConfigLength];
					Config = _config;

					_dataHandle = new HudNodeData[1];
					_dataHandle[0].Item1 = _config;
					_dataHandle[0].Item2 = new HudSpaceOriginFunc[1];
					_dataHandle[0].Item3.Item1 = GetOrSetApiMember;
					_dataHandle[0].Item3.Item5 = BeginLayout;

					_dataHandle[0].Item4 = null;
					_dataHandle[0].Item5 = childHandles;
					DataHandle = _dataHandle;

					_config[VisMaskID] = (uint)HudElementStates.IsVisible;
					_config[InputMaskID] = (uint)HudElementStates.IsInputEnabled;
					_config[StateID] = (uint)(HudElementStates.IsRegistered | HudElementStates.IsInputEnabled | HudElementStates.IsVisible);
			

					Type nodeType = GetType();

					if (!HookCanary.TypeHookMap.ContainsKey(nodeType))
						HookCanary.AddType(this, nodeType);

					HookUsages usages = HookCanary.TypeHookMap[nodeType];

					if (usages.IsInputDepthCustom)
						_dataHandle[0].Item3.Item2 = InputDepth;

					if (usages.IsHandleInputCustom)
					{
						_dataHandle[0].Item3.Item3 = BeginInput;
						_config[StateID] |= (uint)HudElementStates.IsInputHandlerCustom;
					}

					if (usages.IsMeasureCustom)
						_dataHandle[0].Item3.Item4 = Measure;

					if (usages.IsLayoutCustom)
						_config[StateID] |= (uint)HudElementStates.IsLayoutCustom;

					if (usages.IsDrawCustom)
						_dataHandle[0].Item3.Item6 = Draw;
				}
			}


			protected virtual void BeginInput()
			{
				if ((Config[StateID] & (uint)HudElementStates.IsInputHandlerCustom) > 0)
				{
					Vector3 cursorPos = HudSpace.CursorPos;
					HandleInput(new Vector2(cursorPos.X, cursorPos.Y));
				}
			}


			protected virtual void BeginLayout(bool _)
			{
				if (HudSpace != null)
					_config[StateID] |= (uint)HudElementStates.IsSpaceNodeReady;
				else
					_config[StateID] &= ~(uint)HudElementStates.IsSpaceNodeReady;

				if ((Config[StateID] & (uint)HudElementStates.IsLayoutCustom) > 0)
					Layout();
			}


			protected virtual void Measure()
			{ }


			protected virtual void Layout()
			{ }


			protected virtual void Draw()
			{ }


			protected virtual void InputDepth()
			{ }


			protected virtual void HandleInput(Vector2 cursorPos)
			{ }


			public virtual bool RegisterChild(HudNodeBase child)
			{
				if (child.Parent == this && !child.Registered)
				{
					child._dataHandle[0].Item4 = DataHandle;
					child.HudSpace = HudSpace;

					children.Add(child);
					childHandles.Add(child.DataHandle);

					if ((Config[StateID] & Config[VisMaskID]) == Config[VisMaskID])
					{
						uint[] rootConfig = HudMain.Instance._root._config;
						bool isActive = Math.Abs((int)Config[FrameNumberID] - (int)rootConfig[FrameNumberID]) < 2;

						if (isActive && (rootConfig[StateID] & (uint)HudElementStates.IsStructureStale) == 0)
						{
							rootConfig[StateID] |= (uint)HudElementStates.IsStructureStale;
						}
					}

					return true;
				}

				else if (child.Parent == null)
					return child.Register(this);
				else
					return false;
			}


			public virtual bool RemoveChild(HudNodeBase child)
			{
				if (child.Parent == this)
					return child.Unregister();

				else if (child.Parent == null)
				{
					child._dataHandle[0].Item4 = null;
					childHandles.Remove(child.DataHandle);
					return children.Remove(child);
				}
				else
					return false;
			}


			protected virtual object GetOrSetApiMember(object data, int memberEnum)
			{
				switch ((HudElementAccessors)memberEnum)
				{
					case HudElementAccessors.GetType:

						return GetType();
					case HudElementAccessors.ZOffset:
						return (sbyte)ZOffset;
					case HudElementAccessors.FullZOffset:
						return (ushort)Config[FullZOffsetID];
					case HudElementAccessors.Position:
						return Vector2.Zero;
					case HudElementAccessors.Size:
						return Vector2.Zero;
					case HudElementAccessors.GetHudSpaceFunc:
						return HudSpace?.GetHudSpaceFunc;
					case HudElementAccessors.ModName:
						return ExceptionHandler.ModName;
					case HudElementAccessors.LocalCursorPos:
						return HudSpace?.CursorPos ?? Vector3.Zero;
					case HudElementAccessors.PlaneToWorld:
						return HudSpace?.PlaneToWorldRef[0] ?? default(MatrixD);
					case HudElementAccessors.IsInFront:
						return HudSpace?.IsInFront ?? false;
					case HudElementAccessors.IsFacingCamera:
						return HudSpace?.IsFacingCamera ?? false;
					case HudElementAccessors.NodeOrigin:
						return HudSpace?.PlaneToWorldRef[0].Translation ?? Vector3D.Zero;
				}

				return null;
			}
		}
	}
}