using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace DisplayApps
{
    public static class CombinedBar
    {
        public static void Draw(MySpriteDrawFrame frame, RectangleF bar, float storageRatio, Color storageColor, float netFlow, float maxFlow, float S, float Top)
        {
            storageRatio = MathHelper.Clamp(storageRatio, 0f, 1f);
            // background
            frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(bar.Center.X, bar.Center.Y + Top), Size = bar.Size, Color = new Color(22, 26, 36), Alignment = TextAlignment.CENTER });
            // storage fill from left
            float fillW = bar.Size.X * storageRatio;
            if (fillW > 0.5f)
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(bar.X + fillW / 2f, bar.Center.Y + Top), Size = new Vector2(fillW, bar.Size.Y), Color = storageColor, Alignment = TextAlignment.CENTER });
            float netH = bar.Size.Y * 0.38f * 0.5f; // 0.5x smaller as requested
            if (netH < 4f * S) netH = 4f * S;
            float topY = bar.Y - netH / 2f - 2f * S; // net sitting on top of percentage bar
            // net flow 0.5x smaller, 2px black border
            if (maxFlow > 0.0001f && System.Math.Abs(netFlow) > 0.005f)
            {
                float ratio = MathHelper.Clamp(netFlow / maxFlow, -1f, 1f);
                float halfW = bar.Size.X * 0.5f;
                float flowW = System.Math.Abs(ratio) * halfW * 0.225f; // 0.5x
                if (flowW < 3f * S) flowW = 3f * S;
                Color c = ratio < 0f ? new Color(230, 60, 50) : new Color(50, 210, 90);
                Vector2 fc = ratio < 0f ? new Vector2(bar.Center.X - flowW / 2f - 1f * S, topY + Top) : new Vector2(bar.Center.X + flowW / 2f + 1f * S, topY + Top);
                Vector2 borderSize = new Vector2(flowW + 4f * S, netH + 4f * S);
                Vector2 fillSize = new Vector2(flowW, netH);
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = fc, Size = borderSize, Color = new Color(0, 0, 0), Alignment = TextAlignment.CENTER });
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = fc, Size = fillSize, Color = c, Alignment = TextAlignment.CENTER });
            }
            // white full height of % bar + net bar (as requested, not small), never removed, on top of both
            float whiteTop = topY - netH / 2f - 2f * S; // top of net border
            float whiteBottom = bar.Y + bar.Size.Y; // bottom of % bar
            float whiteH = whiteBottom - whiteTop;
            float whiteY = (whiteTop + whiteBottom) / 2f + Top;
            frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(bar.Center.X, whiteY), Size = new Vector2(2f, whiteH), Color = new Color(220, 230, 240), Alignment = TextAlignment.CENTER });
            frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareHollow", Position = new Vector2(bar.Center.X, bar.Center.Y + Top), Size = bar.Size, Color = new Color(80, 90, 105), Alignment = TextAlignment.CENTER });
            // also hollow for net bar background? not needed, net bar has its own border
        }
    }
}
