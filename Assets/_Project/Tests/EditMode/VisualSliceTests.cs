using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】Visual Vertical Slice（VS ステージ）とアートの自動取り込み（ArtSetup・ArtImportPostprocessor）。
    /// 見た目はルールに関係しないこと、盤面データから層ごとに置かれることを確かめる。
    /// </summary>
    public class VisualSliceTests
    {
        const string ThemePath = "Assets/_Project/Data/Themes/RuinsTheme.asset";
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        static StageData Vs() => AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageVS.asset");

        [Test]
        public void ThemeHasEveryElementTheSliceNeeds()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            Assert.IsNotNull(theme, "ArtSetup がテーマを作っている");
            foreach (var key in new[] { "floor_a", "floor_b", "wall", "wall_shadow", "spike", "warp_a", "warp_b", "rune", "fire", "danger", "fire_next", "bomb", "shadow", "background", "frame_board", "brazier", "vfx_slash", "vfx_hit", "vfx_explosion", "vfx_death", "vfx_dust" })
                Assert.IsNotNull(theme.Get(key), key);
            foreach (var kind in new[] { "player", "swordsman", "bomber", "archer", "spearman", "mirror", "knight", "normal" })
            {
                var u = theme.units.FirstOrDefault(x => x.kind == kind);
                Assert.IsNotNull(u, kind);
                Assert.IsTrue(u.down != null && u.up != null && u.left != null && u.right != null, kind + " の4方向");
            }
            Assert.IsNotNull(theme.flashMaterial);
            foreach (var ui in new[] { "card_attack", "card_move", "card_control", "card_support", "cardart_slash", "cardart_pull", "portrait_player", "kind_bomber", "panel", "button_gold", "turn" })
                Assert.IsNotNull(theme.Ui(ui), ui);
            Assert.AreEqual(24, theme.UiBorder("card_attack"), "カード枠は 9 スライス（ふち 24）");
            // 連番は Animator つき、1回きりのエフェクトは消える
            Assert.IsNotNull(theme.Get("vfx_slash").GetComponent<Animator>().runtimeAnimatorController);
            Assert.Greater(theme.Get("vfx_slash").GetComponent<OneShotVfx>().lifetime, 0f);
            Assert.IsNull(theme.Get("fire").GetComponent<OneShotVfx>(), "炎はループ");
        }

        [Test]
        public void ImportedArtIsConfiguredAutomatically()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Art/Generated/unit_player_down.png");
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType);
            Assert.AreEqual(128f, ti.spritePixelsPerUnit, "1マス＝128px＝1ユニット");
            Assert.IsTrue(ti.mipmapEnabled, "【RC3】縮めて描くユニットの絵はミップマップ（720p でも縁がざらつかない）");
            Assert.IsFalse(((TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Art/Generated/frame_board_9s64.png")).mipmapEnabled, "枠（9スライス）はミップマップなし");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Generated/tile_wall.png");
            Assert.AreEqual(1f, sprite.bounds.size.x, 0.01f, "床・壁は1マスぴったり");
        }

        [Test]
        public void PrefabsAreOnTheirSortingLayers()
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            string Layer(string key) => theme.Get(key).GetComponent<SpriteRenderer>().sortingLayerName;
            Assert.AreEqual(SortLayers.Floor, Layer("floor_a"));
            Assert.AreEqual(SortLayers.Hazard, Layer("wall"));
            Assert.AreEqual(SortLayers.Hazard, Layer("spike"));
            Assert.AreEqual(SortLayers.Telegraph, Layer("danger"));
            Assert.AreEqual(SortLayers.Units, Layer("shadow"));
            Assert.AreEqual(SortLayers.Vfx, Layer("vfx_hit"));
            Assert.AreEqual(SortLayers.Background, Layer("background"));
        }

        [Test]
        public void VsStageUsesTheThemeAndTheRulesStayTheSame()
        {
            var s = Vs();
            Assert.IsNotNull(s);
            Assert.IsNotNull(s.visualTheme, "VS だけテーマを使う");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageV4.asset").visualTheme, "V4 も見本（古代遺跡）");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageV1.asset").visualTheme, "他の V 系はグレーBOX のまま");
            var m = BattleModel.FromStage(s);
            Assert.AreEqual(2, m.Enemies.Count());
            Assert.IsTrue(m.Enemies.Any(e => e.Kind == EnemyKind.Bomber) && m.Enemies.Any(e => e.Kind == EnemyKind.Swordsman));
            Assert.AreEqual(2, m.Board.Spikes.Count());
            Assert.AreEqual(2, m.Board.Warps.Count, "ワープ陣1組（2マス）");
        }

        [Test]
        public void ThemedBoardIsBuiltInLayersFromTheMapData()
        {
            var s = Vs();
            root = new GameObject("GridTest");
            var grid = root.AddComponent<GridManager>();
            grid.BuildFromRows(s.GetMapRows(), s.spikeDamage, s.visualTheme);
            var tiles = root.transform.Find("Tiles");
            int walls = s.GetMapRows().Sum(r => r.Count(ch => ch == '#'));
            Assert.AreEqual(walls, tiles.Cast<Transform>().Count(t => t.name.StartsWith("wall ")), "壁は盤面データの # の数だけ");
            Assert.AreEqual(2, tiles.Cast<Transform>().Count(t => t.name.StartsWith("warp_a ")));
            Assert.IsNotNull(tiles.Find("Background"));
            int floors = tiles.Cast<Transform>().Count(t => t.name.StartsWith("floor_"));
            Assert.AreEqual(grid.Width * grid.Height - walls, floors, "壁以外のマスに床");
            // トゲは HazardTile の下にテーマの絵
            var spike = tiles.GetComponentsInChildren<HazardTile>().First();
            Assert.AreEqual(SortLayers.Hazard, spike.GetComponentInChildren<SpriteRenderer>().sortingLayerName);
            // 予告はテーマの絵（Telegraph 層）
            grid.ShowMarks("bomb", new[] { new Vector2Int(1, 1) }, Color.red);
            var mark = tiles.Cast<Transform>().First(t => t.name == "Mark_bomb");
            Assert.AreEqual(SortLayers.Telegraph, mark.GetComponent<SpriteRenderer>().sortingLayerName);
        }

        [Test]
        public void UnitsGetFourDirectionSpritesAndFollowFacing()
        {
            var s = Vs();
            root = new GameObject("GridTest");
            var grid = root.AddComponent<GridManager>();
            grid.BuildFromRows(s.GetMapRows(), s.spikeDamage, s.visualTheme);
            var player = grid.SpawnUnit("プレイヤー", true, 12, grid.PlayerStart);
            foreach (var start in grid.EnemyStarts) grid.SpawnUnit("敵", false, 14, start);
            grid.Model.ApplyStage(s);
            foreach (var u in grid.Units) u.ApplyTheme(s.visualTheme);
            Assert.IsTrue(grid.Units.All(u => u.Themed));
            var body = player.GetComponentsInChildren<SpriteRenderer>().First(r => r.name == "Body");
            Assert.AreEqual("unit_player_down", body.sprite.name);
            Assert.AreEqual(SortLayers.Units, body.sortingLayerName);
        }

        [Test]
        public void CrystalThemeOverridesTheBoardButKeepsTheGimmicks()
        {
            var crystal = AssetDatabase.LoadAssetAtPath<VisualTheme>("Assets/_Project/Data/Themes/CrystalTheme.asset");
            var ruins = AssetDatabase.LoadAssetAtPath<VisualTheme>(ThemePath);
            Assert.IsNotNull(crystal);
            string FloorPath(VisualTheme t) => AssetDatabase.GetAssetPath(t.Get("floor_a").GetComponent<SpriteVariant>().sprites[0]);
            StringAssert.Contains("/crystal/", FloorPath(crystal), "紫晶神殿の床");
            StringAssert.DoesNotContain("/crystal/", FloorPath(ruins));
            Assert.AreEqual(AssetDatabase.GetAssetPath(ruins.Get("spike").GetComponent<SpriteRenderer>().sprite),
                AssetDatabase.GetAssetPath(crystal.Get("spike").GetComponent<SpriteRenderer>().sprite), "トゲ（情報の絵）はどのテーマでも同じ");
            var vb = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageVB.asset");
            Assert.AreEqual(crystal, vb.visualTheme, "ボス戦は紫晶神殿");
            Assert.AreEqual(55, vb.enemyMaxHp, "ボス案 B：HP 55");
        }

        [Test]
        public void RefillSevenKeepsSevenCardsInHand()
        {
            var cards = new System.Collections.Generic.List<CommandData>();
            for (int i = 0; i < 20; i++) { var c = ScriptableObject.CreateInstance<CommandData>(); c.displayName = "c" + i; cards.Add(c); }
            var d = new DeckState(new System.Random(1), handSize: DeckState.SevenHand, policy: DrawPolicy.RefillSeven);
            d.StartBattle(cards);
            Assert.AreEqual(7, d.Hand.Count, "初手7");
            d.UseCards(new[] { d.Hand[0], d.Hand[1], d.Hand[2] });
            d.EndTurn(new CardInstance[0]);
            d.StartTurnDraw();
            Assert.AreEqual(7, d.Hand.Count, "使った分を補充して7枚");
            d.StartTurnDraw();
            Assert.AreEqual(7, d.Hand.Count, "上限 7");
            foreach (var c in cards) Object.DestroyImmediate(c);
        }
    }
}
