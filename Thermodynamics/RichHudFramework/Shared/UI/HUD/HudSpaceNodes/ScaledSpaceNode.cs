using System;
namespace RichHudFramework
{
	namespace UI
	{
		public class ScaledSpaceNode : HudSpaceNodeBase
		{
			public float PlaneScale { get; set; } = 1f;

			public Func<float> UpdateScaleFunc { get; set; }

/// <summary>ScaledSpaceNode operation.</summary>
			public ScaledSpaceNode(HudParentBase parent = null) : base(parent)
			{ }

/// <summary>Layout operation.</summary>
			protected override void Layout()
			{
				if (UpdateScaleFunc != null)
/// <summary>UpdateScaleFunc operation.</summary>
					PlaneScale = UpdateScaleFunc();

				IReadOnlyHudSpaceNode parentSpace = Parent.HudSpace;

				PlaneToWorldRef[0] = parentSpace.PlaneToWorldRef[0];
				PlaneToWorldRef[0].Right *= PlaneScale;
				PlaneToWorldRef[0].Up *= PlaneScale;

				IsInFront = parentSpace.IsInFront;
				IsFacingCamera = parentSpace.IsFacingCamera;

				CursorPos = parentSpace.CursorPos / PlaneScale;
			}
		}
	}
}