using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// RUN の戦闘以外の画面（RUN 開始・カード報酬・休憩・RUN CLEAR・RUN 失敗）。仮 UI（IMGUI）。
    /// 戦闘中の画面は BattleUI が描く。
    /// </summary>
    public class RunUI : MonoBehaviour
    {
        [SerializeField] RunManager run;

        static readonly Color Background = new Color(0.1f, 0.1f, 0.12f, 1f);

        void OnGUI()
        {
            if (run == null || !run.InRun || run.Screen == RunScreen.Battle) return;
            Setup();
            float scale = ApplyScale();
            float w = Screen.width / scale, h = RefHeight;
            DrawRect(new Rect(0, 0, w, h), Background);
            GUI.depth = -10;

            switch (run.Screen)
            {
                case RunScreen.RunStart: DrawRunStart(w, h); break;
                case RunScreen.Reward: DrawReward(w, h); break;
                case RunScreen.Rest: DrawRest(w, h); break;
                case RunScreen.Clear: DrawEnd(w, h, true); break;
                case RunScreen.Failed: DrawEnd(w, h, false); break;
            }
        }

        void DrawTitle(float w, string text, Color color)
        {
            var old = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(0, 24, w, 44), text, new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter, fontSize = 32 });
            GUI.contentColor = old;
        }

        void DrawRunStart(float w, float h)
        {
            DrawTitle(w, "RUN（デッキ構築 × 連戦）縦切り版", Color.white);
            string rules =
                "・HP とデッキを引き継いで3戦を勝ち抜く。HP は戦闘が終わっても回復しない（休憩で少し回復）。\n" +
                "・毎ターン手札は5枚。「歩く」はカードではなく何回でも使える（ただし命令スロットを1枠使う）。\n" +
                "・命令は最大3つ。3つ使えば大きく動けるが、敵にも3つの命令が渡る（行動回数＝敵へ渡す命令数）。\n" +
                "・使ったカードは捨て札へ。使わなかったカードは2枚まで「保留」して次のターンに残せる（残りは捨て札）。\n" +
                "・山札が切れたら捨て札をシャッフル。戦闘に勝つとカード報酬（3枚から1枚、または取らない）。\n" +
                "・カードの効果は敵にもまねされる。強いカードほど、敵に使われたときも強い。";
            GUI.Label(new Rect(w * 0.08f, 84, w * 0.84f, 170), rules, new GUIStyle(Small) { fontSize = 16 });

            GUI.Label(new Rect(w * 0.08f, 262, w * 0.84f, 24), $"進行：BATTLE 1 → 報酬 → BATTLE 2 → 報酬 → 休憩（HP +{run.RestHeal}）→ BATTLE 3 → RUN CLEAR　　HP {run.MaxHp}", Label);
            GUI.Label(new Rect(w * 0.08f, 292, w * 0.84f, 24), "初期デッキ", Label);
            DrawCardRow(run.InitialDeck, w * 0.08f, 320, w * 0.84f, 150);

            if (GUI.Button(new Rect(w * 0.5f - 160, h - 120, 320, 56), "RUN 開始", new GUIStyle(Button) { fontSize = 22 })) { Sfx.Play(Sfx.Kind.Button); run.StartRun(); }
            if (GUI.Button(new Rect(w * 0.5f - 110, h - 56, 220, 36), "チュートリアルへ戻る", Button)) run.ExitToTutorial();
        }

        void DrawReward(float w, float h)
        {
            var state = run.Run;
            DrawTitle(w, $"BATTLE {state.BattleIndex + 1} 勝利！　カード報酬", Color.yellow);
            GUI.Label(new Rect(0, 72, w, 26), $"1枚選んでデッキに加える（取らなくてもよい）。　HP {state.Hp}/{state.MaxHp}　デッキ {state.Deck.Count} 枚", new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter });

            var options = state.RewardOptions;
            float cw = 240, ch = 300, gap = 30;
            float startX = w * 0.5f - (options.Count * cw + (options.Count - 1) * gap) * 0.5f;
            for (int i = 0; i < options.Count; i++)
            {
                var rect = new Rect(startX + i * (cw + gap), 110, cw, ch);
                if (DrawCard(rect, options[i], "クリックで取る", true, false, false, true)) { Sfx.Play(Sfx.Kind.Card); run.TakeReward(options[i]); return; }
            }
            if (GUI.Button(new Rect(w * 0.5f - 130, 430, 260, 48), "取らない（デッキを太らせない）", Button)) { Sfx.Play(Sfx.Kind.Button); run.SkipReward(); return; }

            GUI.Label(new Rect(w * 0.08f, 500, w * 0.84f, 24), "今のデッキ", Label);
            GUI.Label(new Rect(w * 0.08f, 526, w * 0.84f, 60), DeckSummary(state.Deck), Small);
        }

        void DrawRest(float w, float h)
        {
            var state = run.Run;
            DrawTitle(w, "休憩", new Color(0.6f, 1f, 0.7f));
            int after = Mathf.Min(state.MaxHp, state.Hp + run.RestHeal);
            GUI.Label(new Rect(0, 150, w, 40), $"HP {state.Hp} / {state.MaxHp}　→　{after} / {state.MaxHp}（+{after - state.Hp}）", new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
            GUI.Label(new Rect(0, 200, w, 26), $"次は最後の戦闘（BATTLE {state.BattleIndex + 2}）。", new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(w * 0.5f - 140, 260, 280, 56), $"休む（HP +{run.RestHeal}）", new GUIStyle(Button) { fontSize = 20 })) { Sfx.Play(Sfx.Kind.Button); run.Rest(); return; }
            GUI.Label(new Rect(w * 0.08f, 360, w * 0.84f, 24), "今のデッキ", Label);
            GUI.Label(new Rect(w * 0.08f, 386, w * 0.84f, 60), DeckSummary(state.Deck), Small);
        }

        void DrawEnd(float w, float h, bool cleared)
        {
            var state = run.Run;
            DrawTitle(w, cleared ? "RUN CLEAR!" : $"RUN 失敗（BATTLE {state.BattleIndex + 1}）", cleared ? Color.yellow : Color.gray);
            var lines = new List<string>();
            for (int i = 0; i < state.TurnsPerBattle.Count; i++)
                lines.Add($"BATTLE {i + 1}：{state.TurnsPerBattle[i]} ターン　受けたダメージ {state.DamagePerBattle[i]}");
            lines.Add($"最後の HP：{state.Hp} / {state.MaxHp}");
            lines.Add($"報酬：{string.Join(" ／ ", state.RewardHistory)}");
            lines.Add($"最後のデッキ（{state.Deck.Count} 枚）：{DeckSummary(state.Deck)}");
            GUI.Label(new Rect(w * 0.12f, 100, w * 0.76f, 260), string.Join("\n", lines), new GUIStyle(Small) { fontSize = 17 });
            if (GUI.Button(new Rect(w * 0.5f - 160, h - 150, 320, 52), "もう一度 RUN", new GUIStyle(Button) { fontSize = 20 })) { run.OpenRunStart(); return; }
            if (GUI.Button(new Rect(w * 0.5f - 110, h - 86, 220, 36), "チュートリアルへ戻る", Button)) run.ExitToTutorial();
        }

        static string DeckSummary(IReadOnlyList<CommandData> deck) =>
            string.Join("　", deck.GroupBy(c => c.displayName).Select(g => $"{g.Key}×{g.Count()}"));

        static void DrawCardRow(IReadOnlyList<CommandData> cards, float x, float y, float width, float height)
        {
            if (cards.Count == 0) return;
            float gap = 6, cw = Mathf.Min(110, (width - gap * (cards.Count - 1)) / cards.Count);
            for (int i = 0; i < cards.Count; i++)
                DrawCard(new Rect(x + i * (cw + gap), y, cw, height), cards[i], "", false, false, false);
        }
    }
}
