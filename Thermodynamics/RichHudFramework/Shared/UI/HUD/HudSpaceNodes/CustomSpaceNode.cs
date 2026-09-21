using System;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		public class CustomSpaceNode : HudSpaceNodeBase
		{
			public Func<MatrixD> UpdateMatrixFunc { get; set; }

/// <summary>CustomSpaceNode operation.</summary>
			public CustomSpaceNode(HudParentBase parent = null) : base(parent)
			{ }

/// <summary>Layout operation.</summary>
			protected override void Layout()
			{
				if (UpdateMatrixFunc != null)
/// <summary>UpdateMatrixFunc operation.</summary>
					PlaneToWorldRef[0] = UpdateMatrixFunc();
/// <summary>if operation.</summary>
				else if (Parent?.HudSpace != null)
					PlaneToWorldRef[0] = Parent.HudSpace.PlaneToWorld;

				base.Layout();
			}
		}
	}
}