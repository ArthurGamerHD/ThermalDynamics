using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using HudNodeHookData = VRage.MyTuple<
	System.Func<object, int, object>, // 1 -  GetOrSetApiMemberFunc
	System.Action, // 2 - InputDepthAction
	System.Action, // 3 - InputAction
	System.Action, // 4 - SizingAction
	System.Action<bool>, // 5 - LayoutAction
	System.Action // 6 - DrawAction
>;
using HudSpaceOriginFunc = System.Func<VRageMath.Vector3D>;

namespace RichHudFramework
{
	using HudNodeData = MyTuple<
		uint[], // 1 - Config { 1.0 - State, 1.1 - NodeVisibleMask, 1.2 - NodeInputMask, 1.3 - zOffset, 1.4 - zOffsetInner, 1.5 - fullZOffset }
		HudSpaceOriginFunc[],  // 2 - GetNodeOriginFunc
		HudNodeHookData, // 3 - Main hooks
		object, // 4 - Parent as HudNodeDataHandle
		List<object>, // 5 - Children as IReadOnlyList<HudNodeDataHandle>
		object // 6 - Unused
	>;

	namespace UI
	{
		using static RichHudFramework.UI.NodeConfigIndices;

		using HudNodeDataHandle = IReadOnlyList<HudNodeData>;

		public static class NodeConfigIndices
		{
			public const int StateID = 0;

			public const int VisMaskID = 1;

			public const int InputMaskID = 2;

			public const int ZOffsetID = 3;

			public const int ZOffsetInnerID = 4;

			public const int FullZOffsetID = 5;

			public const int FrameNumberID = 6;

			public const int ConfigLength = 7;
		}

		public abstract partial class HudParentBase
		{
			protected static partial class ParentUtils
			{
				private struct LinkedHudNode
				{
					public LinkedHudNode Parent => new LinkedHudNode { dataRef = (dataRef[0].Item4 as HudNodeDataHandle) };

					public HudElementStates State => (HudElementStates)dataRef[0].Item1[StateID];

					public HudElementStates NodeVisibleMask => (HudElementStates)dataRef[0].Item1[VisMaskID];

					public HudElementStates NodeInputMask => (HudElementStates)dataRef[0].Item1[InputMaskID];

					public sbyte ZOffset => (sbyte)dataRef[0].Item1[ZOffsetID];

					public byte ZOffsetInner => (byte)dataRef[0].Item1[ZOffsetInnerID];

					public ushort FullZOffset => (ushort)dataRef[0].Item1[FullZOffsetID];

					public Action InputDepthCallback => dataRef[0].Item3.Item2;

					public Action HandleInputCallback => dataRef[0].Item3.Item3;

					public Action UpdateSizeCallback => dataRef[0].Item3.Item4;

					public Action<bool> LayoutCallback => dataRef[0].Item3.Item5;

					public Action DrawCallback => dataRef[0].Item3.Item6;

					public HudSpaceOriginFunc GetHudNodeOriginFunc => dataRef[0].Item2[0];

					public ApiMemberAccessor GetOrSetMemberFunc => dataRef[0].Item3.Item1;

					public HudNodeDataHandle dataRef;
				}
			}
		}
	}
}