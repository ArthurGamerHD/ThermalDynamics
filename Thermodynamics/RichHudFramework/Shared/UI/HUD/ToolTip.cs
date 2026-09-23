using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using RichStringMembers = VRage.MyTuple<System.Text.StringBuilder, VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>>;

namespace RichHudFramework
{
    namespace UI
    {
        using ToolTipMembers = MyTuple<
            List<RichStringMembers>,
            Color?
        >;

		public class ToolTip
        {
            public static readonly GlyphFormat DefaultText = GlyphFormat.Blueish.WithSize(.75f);
            public static readonly Color 

                DefaultBG = new Color(73, 86, 95),

                OrangeWarningBG = new Color(180, 110, 0),

                RedWarningBG = new Color(126, 39, 44);

            public RichText text;

            public Color? bgColor;

            public readonly Func<ToolTipMembers> GetToolTipFunc;


            public ToolTip()
            {
                bgColor = DefaultBG;

                GetToolTipFunc = () => new ToolTipMembers()
                {
                    Item1 = text?.apiData,
                    Item2 = bgColor,
                };
            }


            public ToolTip(Func<ToolTipMembers> GetToolTipFunc)
            {
                bgColor = DefaultBG;
                this.GetToolTipFunc = GetToolTipFunc;
            }


            public static implicit operator ToolTip(RichText text) =>

                new ToolTip() { text = text };


            public static implicit operator ToolTip(string text) =>

                new ToolTip() { text = new RichText(text, DefaultText) };
        }
    }
}
