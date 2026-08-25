namespace WalkGame.Core
{
    /// <summary>
    /// Contract for canonical state objects that keep a derived in-memory index which is
    /// not part of their serialized form. State-copy tools (save rollback, future cloud
    /// merge) must call <see cref="Repair"/> after populating the serialized fields so
    /// the index cannot silently disagree with the canonical data (ActivityModels S8).
    /// </summary>
    public interface IPostCopyRepair
    {
        void Repair();
    }
}
