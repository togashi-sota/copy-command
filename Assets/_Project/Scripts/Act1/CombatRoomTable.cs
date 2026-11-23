namespace CopyCommand
{
    /// <summary>
    /// 【戦闘中心 v3（2026-10-06）】戦闘中心 ACT1 v3 の部屋の一覧（扉に出す名前・一言・コピーの使いどころ・報酬の傾向・速さの目安）。
    /// 盤面と数値は tools/combat_rooms.txt（asset= の部屋を run-setup がアセットにする）。
    /// 9人のデザイナー（AI）が 80 案以上を出し、ボット4種で測って 19 部屋を選んだ（理由は docs/research/COMBAT_ACT1_V3.md）。
    /// 速さの目安＝読み手ボットの平均ターン（四捨五入）。危険戦は任意ミッション。
    /// </summary>
    public static class CombatRoomTable
    {
        public static readonly RoomDef[] All =
        {
            // ───── 通常戦・入門（1階：3つから2つが扉に出る） ─────
            new RoomDef { Id = "k_quad", Name = "四人組", Kind = RoomKind.Battle, Stage = "StageK1", CombatRun = true, Intro = true, RewardTags = CardTag.Attack | CardTag.Move, ParTurns = 5,
                Flavor = "固まった4人の兵。全員があなたの札をまねる", CopyTip = "横で空振り：兵どうしが斬り合う" },
            new RoomDef { Id = "k_chase", Name = "追跡者", Kind = RoomKind.Battle, Stage = "StageK2", CombatRun = true, Intro = true, RewardTags = CardTag.Move | CardTag.Attack, ParTurns = 5,
                Flavor = "寄ってくる追跡兵と、同じ行の兵2体", CopyTip = "壁に寄せて空振り／追跡兵をトゲへ" },
            new RoomDef { Id = "k_barrel", Name = "爆薬樽", Kind = RoomKind.Battle, Stage = "StageK3", CombatRun = true, Intro = true, RewardTags = CardTag.Attack | CardTag.Env, ParTurns = 4,
                Flavor = "樽の真下に槍兵。樽の上に兵2体", CopyTip = "槍兵にまねで樽を叩かせる" },
            // ───── 通常戦（2・3階） ─────
            new RoomDef { Id = "k_shield", Name = "盾列の回廊", Kind = RoomKind.Battle, Stage = "StageK4", CombatRun = true, RewardTags = CardTag.Attack | CardTag.Copy, ParTurns = 5,
                Flavor = "前に盾兵、後ろに砲台の兵（技まね）", CopyTip = "後ろの兵に、盾兵の背中を斬らせる" },
            new RoomDef { Id = "k_crystal", Name = "鏡晶の庭", Kind = RoomKind.Battle, Stage = "StageK5", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Attack, ParTurns = 6,
                Flavor = "まねでしか割れない鏡晶が2つ", CopyTip = "兵を鏡晶の横へ。空振りで割らせる" },
            new RoomDef { Id = "k_narrow", Name = "狭い剣の間", Kind = RoomKind.Battle, Stage = "StageK6", CombatRun = true, RewardTags = CardTag.Move | CardTag.Attack, ParTurns = 4,
                Flavor = "あなたと一緒に動く剣兵。狭い5列", CopyTip = "柱で自分だけ止まり、剣兵を引きはがす" },
            new RoomDef { Id = "k_armor", Name = "鎧と壁", Kind = RoomKind.Battle, Stage = "StageK7", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Move, ParTurns = 6,
                Flavor = "直接は1しか通らない反響鎧兵。壁際にいる", CopyTip = "壁ズレで兵を鎧兵の前へ・まね攻撃で鎧を割る" },
            new RoomDef { Id = "k_spear", Name = "槍と追跡者", Kind = RoomKind.Battle, Stage = "StageK8", CombatRun = true, RewardTags = CardTag.Attack | CardTag.Move, ParTurns = 4,
                Flavor = "あなたの最後の向きへ突く槍兵2体", CopyTip = "最後の1枚の向きで、槍を寄ってきた敵へ" },
            // ───── 戦術戦（5・6階：全滅＋仕掛け1つ） ─────
            new RoomDef { Id = "k_turret", Name = "砲台と鎧", Kind = RoomKind.Tactical, Stage = "StageKT1", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Attack, ParTurns = 6,
                Flavor = "写し砲（倒れない）と、移動だけまねる反響鎧兵", CopyTip = "鎧兵を射線へ歩かせ、写し砲に撃たせる" },
            new RoomDef { Id = "k_statue", Name = "石像の回廊", Kind = RoomKind.Tactical, Stage = "StageKT2", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Env, ParTurns = 5,
                Flavor = "まね攻撃で回る石像。今は空を向いている", CopyTip = "石像を1回回すと、ずっと敵の列を撃つ" },
            new RoomDef { Id = "k_core", Name = "共鳴の核", Kind = RoomKind.Tactical, Stage = "StageKT3", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Multi, ParTurns = 5,
                Flavor = "2つの鏡核と、核の下の番兵（技まね）", CopyTip = "あなたと番兵で同時に叩く→共鳴（全員に3・動けない）" },
            new RoomDef { Id = "k_twins", Name = "鎧の双子", Kind = RoomKind.Tactical, Stage = "StageKT4", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Attack, ParTurns = 5,
                Flavor = "互いを斬れる2体の反響鎧兵と剣兵", CopyTip = "全まねの鎧兵を、技まねの鎧兵の前へ" },
            // ───── 試練（3階：短いパズル・失敗しても負けではない） ─────
            new RoomDef { Id = "k_trial_relay", Name = "足は像、技は砲", Kind = RoomKind.Trial, Stage = "StageKE1", CombatRun = true, RewardTags = CardTag.Move | CardTag.Copy,
                Flavor = "運び像で扉を開け、写し砲で封印を割って出口へ（3ターン）", CopyTip = "移動の札＝像、技の札＝砲" },
            new RoomDef { Id = "k_trial_line", Name = "出口は射線の上", Kind = RoomKind.Trial, Stage = "StageKE2", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Attack,
                Flavor = "写し砲で封印石を割って出口へ（3ターン）", CopyTip = "敵がまねるのは、あなたの命令の後" },
            new RoomDef { Id = "k_trial_split", Name = "柱で分かれる二体", Kind = RoomKind.Trial, Stage = "StageKE3", CombatRun = true, RewardTags = CardTag.Move,
                Flavor = "2体の運び像を、同時に運び紋へ（3ターン）", CopyTip = "柱で片方だけ止める" },
            // ───── 危険戦（6階：報酬が多い） ─────
            new RoomDef { Id = "k_danger_ring", Name = "槍の輪", Kind = RoomKind.Danger, Stage = "StageKD1", CombatRun = true, RewardTags = CardTag.Attack | CardTag.Copy,
                Flavor = "あなたを囲む槍兵4本（技まね）と反響鎧兵2体", CopyTip = "技の向きで、槍を仲間へ向ける", Mission = new Mission { Kind = MissionKind.FriendlyKill } },
            new RoomDef { Id = "k_danger_core", Name = "二つの核と砲兵", Kind = RoomKind.Danger, Stage = "StageKD2", CombatRun = true, RewardTags = CardTag.Copy | CardTag.Multi,
                Flavor = "反響鎧兵3体と砲兵の十字。鏡核の共鳴で鎧を一斉に割る", CopyTip = "鏡核を同じターンに（1つ以上はまね）", Mission = new Mission { Kind = MissionKind.FastClear, Turns = 6 } },
            new RoomDef { Id = "k_danger_mend", Name = "癒し手の砦", Kind = RoomKind.Danger, Stage = "StageKD3", CombatRun = true, RewardTags = CardTag.Attack | CardTag.Copy,
                Flavor = "仲間を回復する癒し手と、反響鎧兵・槍兵", CopyTip = "癒し手に、隣の仲間を殴らせる", Mission = new Mission { Kind = MissionKind.FriendlyKill } },
            // ───── ボス（8階） ─────
            new RoomDef { Id = "k_boss", Name = "鏡の主", Kind = RoomKind.Boss, Stage = "StageKB", CombatRun = true,
                Flavor = "殴れば減る。写し身（段階2）と鏡核（段階3）がまねの弱点", CopyTip = "写し身に主を斬らせる／核を同時に叩く" },
        };
    }
}
