namespace Concafe.Simulation.Model
{
    /// <summary>ゲームが終わる理由。10 年完走は Ending 側で扱い、ここには含めない。</summary>
    public enum GameOverReason
    {
        None,
        /// <summary>資金がマイナスになった。</summary>
        CashShortage,
        /// <summary>放火により店舗が焼失した。イースターエッグ由来の強制バッドエンド。</summary>
        Arson,
    }

    /// <summary>
    /// 疲労限界を超えた離脱の結末を、イースターエッグが差し替えるかどうか。
    /// </summary>
    public enum DepartureOverride
    {
        /// <summary>通常どおり。飛ぶか転生する。</summary>
        None,
        /// <summary>店舗に放火して去る。強制バッドエンド。</summary>
        BurnDown,
    }

    /// <summary>
    /// BurnDown を起こす条件。
    ///
    /// 既定は Any（飛ぶ・転生のどちらでも放火する）。RunAwayOnly にすると、
    /// 市場価値が閾値に届かないまま燃え尽きたときだけ放火するようになり、
    /// 「ちゃんと育てれば穏便に去る」という遊びになる。
    /// balance ではなく easter_eggs.json 側で個体ごとに選ぶ。
    /// </summary>
    public enum BurnDownCondition
    {
        Any,
        RunAwayOnly,
    }

    /// <summary>離脱が確定したときの結果。段階 4 の週末処理が組み立てる。</summary>
    public readonly struct DepartureOutcome
    {
        public DepartureKind Kind { get; }
        public DepartureOverride Override { get; }
        public GameOverReason GameOver { get; }
        /// <summary>再生するイベント。未定義なら null。</summary>
        public string EventId { get; }

        public DepartureOutcome(DepartureKind kind, DepartureOverride ovr,
                                GameOverReason gameOver, string eventId)
        {
            Kind = kind;
            Override = ovr;
            GameOver = gameOver;
            EventId = eventId;
        }

        public bool EndsRun => GameOver != GameOverReason.None;

        public static DepartureOutcome Normal(DepartureKind kind) =>
            new DepartureOutcome(kind, DepartureOverride.None, GameOverReason.None, null);
    }
}
