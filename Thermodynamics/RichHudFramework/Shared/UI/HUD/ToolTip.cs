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
            List<RichStringMembers>, // Text
            Color? // BgColor
        >;

		public class ToolTip
        {
            public static readonly GlyphFormat DefaultText = GlyphFormat.Blueish.WithSize(.75f);
            public static readonly Color 
/// <summary>Color operation.</summary>
                DefaultBG = new Color(73, 86, 95),
/// <summary>Color operation.</summary>
                OrangeWarningBG = new Color(180, 110, 0),
/// <summary>Color operation.</summary>
                RedWarningBG = new Color(126, 39, 44);

            public RichText text;

            public Color? bgColor;

            public readonly Func<ToolTipMembers> GetToolTipFunc;

/// <summary>ToolTip operation.</summary>
            public ToolTip()
            {
                bgColor = DefaultBG;

                GetToolTipFunc = () => new ToolTipMembers()
                {
                    Item1 = text?.apiData,
                    Item2 = bgColor,
                };
            }

/// <summary>ToolTip operation.</summary>
            public ToolTip(Func<ToolTipMembers> GetToolTipFunc)
            {
                bgColor = DefaultBG;
                this.GetToolTipFunc = GetToolTipFunc;
            }

/// <summary>ToolTip operation.</summary>
            public static implicit operator ToolTip(RichText text) =>
/// <summary>ToolTip operation.</summary>
                new ToolTip() { text = text };

/// <summary>ToolTip operation.</summary>
            public static implicit operator ToolTip(string text) =>
/// <summary>ToolTip operation.</summary>
                new ToolTip() { text = new RichText(text, DefaultText) };
        }
    }
}
