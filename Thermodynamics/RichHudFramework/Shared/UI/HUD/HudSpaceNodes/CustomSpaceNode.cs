using System;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		public class CustomSpaceNode : HudSpaceNodeBase
		{
			public Func<MatrixD> UpdateMatrixFunc { get; set; }


			public CustomSpaceNode(HudParentBase parent = null) : base(parent)
			{ }


			protected override void Layout()
			{
				if (UpdateMatrixFunc != null)

					PlaneToWorldRef[0] = UpdateMatrixFunc();

				else if (Parent?.HudSpace != null)
					PlaneToWorldRef[0] = Parent.HudSpace.PlaneToWorld;

				base.Layout();
			}
		}
	}
}