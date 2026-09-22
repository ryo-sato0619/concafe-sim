using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    /// <summary>
    /// イースターエッグが離脱の結末を差し替えるかを決める。
    ///
    /// 週末の離脱判定そのもの（疲労が限界を超えたか、飛ぶか転生か）は段階 4 で
    /// 実装する。ここはその結果を受け取って結末だけを差し替える純粋な関数であり、
    /// 先に固めておくことで、段階 4 の判定ロジックと放火イベントが混ざらない。
    /// </summary>
    public static class EasterEggRules
    {
        /// <summary>
        /// 疲労限界を超えて離脱するときの結末。
        ///
        /// 卒業（DepartureKind.Graduated）では呼ばない。放火は「燃え尽きて店を去る」
        /// 瞬間の出来事であり、円満に辞めた相手が火を点けることはない。
        /// </summary>
        public static DepartureOutcome ResolveBurnout(
            Cast cast, DepartureKind natural, EasterEggSet eggs)
        {
            if (natural == DepartureKind.Graduated)
                return DepartureOutcome.Normal(natural);

            var egg = FindEgg(cast, eggs);
            if (egg == null || egg.OnDeparture != DepartureOverride.BurnDown)
                return DepartureOutcome.Normal(natural);

            // RunAwayOnly なら、市場価値が閾値に届かないまま燃え尽きたときだけ燃やす。
            // 育て上げたキャストは穏便に移籍していく。
            if (egg.BurnDownOn == BurnDownCondition.RunAwayOnly && natural != DepartureKind.RanAway)
                return DepartureOutcome.Normal(natural);

            var ev = eggs.FindEvent(egg.EventId);

            // イベントが未実装でも結末は成立させる。ゲームは止まらない。
            var reason = ev != null && ev.GameOver != GameOverReason.None
                ? ev.GameOver
                : GameOverReason.Arson;

            return new DepartureOutcome(natural, DepartureOverride.BurnDown, reason, egg.EventId);
        }

        /// <summary>
        /// キャストに紐づくイースターエッグ。採用時に記録した ID で引く。
        /// 源氏名で引き直さないのは、採用後に定義を書き換えても既存のキャストの
        /// 挙動が変わらないようにするため。
        /// </summary>
        public static EasterEgg FindEgg(Cast cast, EasterEggSet eggs)
        {
            if (eggs == null || string.IsNullOrEmpty(cast?.EasterEggId)) return null;

            foreach (var egg in eggs.Eggs)
                if (egg.Id == cast.EasterEggId) return egg;

            return null;
        }
    }
}
