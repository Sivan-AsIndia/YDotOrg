using YDots.CAM.Domain.Common;

namespace YDots.CAM.Domain.Entities;

/// <summary>
/// A channel a campaign runs on.
///
/// A PURE JOIN ROW, so it stays on <see cref="BaseEntity"/> and takes no audit columns. It is
/// created and destroyed with its campaign and has nothing of its own to say about who edited
/// it; five audit columns per row would record something nobody will ever read.
/// </summary>
public class CampaignChannel : BaseEntity
{
    public Guid CampaignId { get; set; }

    public Guid ChannelId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Channel Channel { get; set; } = default!;
}
