using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// トゲの見た目。ルール（入った瞬間に毎回ダメージ）は RulesEngine と BoardModel が持つ。
    /// </summary>
    public class HazardTile : MonoBehaviour
    {
        public static readonly Color SpikeColor = new Color(1f, 0.85f, 0.1f);

        public Vector2Int Cell { get; private set; }
        public int Damage { get; private set; }

        public void Setup(Vector2Int cell, int damage, Vector3 worldPosition, VisualTheme theme = null)
        {
            Cell = cell;
            Damage = damage;
            transform.position = worldPosition;
            if (theme != null && theme.Spawn("spike", transform, worldPosition) != null) return; // 【検証中】テーマの絵
            GreyboxSprites.CreateShape(transform, "Spike", GreyboxSprites.Triangle, SpikeColor, worldPosition, 0.6f, 1);
        }
    }
}
