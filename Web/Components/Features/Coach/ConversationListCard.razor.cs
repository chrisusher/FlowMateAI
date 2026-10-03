namespace Web.Components.Features.Coach;

public partial class ConversationListCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Coach);
    }


}
