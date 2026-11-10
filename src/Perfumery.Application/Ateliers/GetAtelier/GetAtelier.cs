using Perfumery.Application.Abstractions.Game;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Application.Game;
using Perfumery.Domain.Ateliers;
using Perfumery.SharedKernel;

namespace Perfumery.Application.Ateliers.GetAtelier;

/// <param name="RecentLedgerEntries">How many of the latest ledger entries to return.</param>
public sealed record GetAtelierQuery(int RecentLedgerEntries = 20) : IQuery<Result<AtelierResponse>>;

/// <param name="Cash">Francs in hand.</param>
/// <param name="Equipment">Each kind of equipment and how much of it is free.</param>
/// <param name="Ledger">The most recent entries, newest first.</param>
/// <param name="Now">The game's idea of the current time.</param>
public sealed record AtelierResponse(
    decimal Cash,
    IReadOnlyList<EquipmentResponse> Equipment,
    IReadOnlyList<LedgerEntryResponse> Ledger,
    DateTimeOffset Now);

public sealed record EquipmentResponse(string Kind, int Count, int InUse, int Free, decimal CapacityKg);

public sealed record LedgerEntryResponse(DateTimeOffset At, decimal Amount, string Category, string Reference);

internal sealed class GetAtelierQueryHandler(IGameSession session, IGameClock clock, AtelierSettings settings)
    : IQueryHandler<GetAtelierQuery, Result<AtelierResponse>>
{
    public Task<Result<AtelierResponse>> Handle(GetAtelierQuery query, CancellationToken cancellationToken)
    {
        if (!session.IsStarted)
        {
            return Task.FromResult<Result<AtelierResponse>>(GameErrors.NotStarted);
        }

        var atelier = session.Atelier;
        var equipment = settings.Equipment
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var inUse = session.EquipmentInUse(pair.Key);
                return new EquipmentResponse(
                    pair.Key.ToString(), pair.Value.Count, inUse, pair.Value.Count - inUse, pair.Value.Capacity.Kilograms);
            });

        var ledger = atelier.Ledger
            .Reverse()
            .Take(Math.Max(0, query.RecentLedgerEntries))
            .Select(entry => new LedgerEntryResponse(entry.At, entry.Amount, entry.Category.ToString(), entry.Reference));

        return Task.FromResult<Result<AtelierResponse>>(new AtelierResponse(
            atelier.Cash.Amount, [.. equipment], [.. ledger], atelier.EffectiveNow(clock.UtcNow)));
    }
}
