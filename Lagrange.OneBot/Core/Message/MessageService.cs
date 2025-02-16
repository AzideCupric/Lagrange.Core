using System.Reflection;
using Lagrange.Core;
using Lagrange.Core.Common.Interface.Api;
using Lagrange.Core.Event.EventArg;
using Lagrange.Core.Message;
using Lagrange.Core.Utility.Extension;
using Lagrange.OneBot.Core.Entity.Message;
using Lagrange.OneBot.Core.Network;
using Lagrange.OneBot.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Lagrange.OneBot.Core.Message;

/// <summary>
/// The class that converts the OneBot message to/from MessageEntity of Lagrange.Core
/// </summary>
public sealed class MessageService
{
    private readonly LagrangeWebSvcCollection _service;
    private readonly ContextBase _context;
    private readonly IConfiguration _config;
    
    private static readonly Dictionary<Type, (string, ISegment)> EntityToSegment;

    static MessageService()
    {
        EntityToSegment = new Dictionary<Type, (string, ISegment)>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            var attribute = type.GetCustomAttribute<SegmentSubscriberAttribute>();
            if (attribute != null)
            {
                EntityToSegment[attribute.Entity] = (attribute.Type, (ISegment)type.CreateInstance(false));
            }
        }
    }
    
    public MessageService(BotContext bot, LagrangeWebSvcCollection service, ContextBase context, IConfiguration config)
    {
        _service = service;
        _context = context;
        _config = config;

        var invoker = bot.Invoker;
        
        invoker.OnFriendMessageReceived += OnFriendMessageReceived;
        invoker.OnGroupMessageReceived += OnGroupMessageReceived;
        invoker.OnTempMessageReceived += OnTempMessageReceived;
    }
    
    private void OnFriendMessageReceived(BotContext bot, FriendMessageEvent e)
    {
                
        var record = (MessageRecord)e.Chain;
        _context.Insert(record.MessageHash, record);
        
        var request = new OneBotPrivateMsg(bot.BotUin)
        {
            MessageId = record.MessageHash,
            UserId = e.Chain.FriendUin,
            GroupSender = new OneBotSender
            {
                
            },
            Message = Convert(e.Chain)
        };

        _ =_service.SendJsonAsync(request);
    }
    
    private void OnGroupMessageReceived(BotContext bot, GroupMessageEvent e)
    {
        if (_config.GetValue<bool>("Message:IgnoreSelf") && e.Chain.FriendUin == bot.BotUin) return; // ignore self message
        
        var record = (MessageRecord)e.Chain;
        _context.Insert(record.MessageHash, record);
        
        var request = new OneBotGroupMsg(bot.BotUin, e.Chain.GroupUin ?? 0, Convert(e.Chain),
            e.Chain.GroupMemberInfo ?? throw new Exception("Group member not found"), record.MessageHash);

        _ = _service.SendJsonAsync(request);
    }
    
    private void OnTempMessageReceived(BotContext bot, TempMessageEvent e)
    {
        // TODO: Implement temp msg
    }

    public static List<OneBotSegment> Convert(IEnumerable<IMessageEntity> entities)
    {
        var result = new List<OneBotSegment>();

        foreach (var entity in entities)
        {
            if (EntityToSegment.TryGetValue(entity.GetType(), out var instance))
            {
                result.Add(new OneBotSegment(instance.Item1, instance.Item2.FromEntity(entity)));
            }
        }

        return result;
    }
}