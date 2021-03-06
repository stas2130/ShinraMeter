﻿using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Tera.Game.Messages;

namespace DamageMeter
{
    public class Chat
    {
        private static Chat _instance;

        private readonly LinkedList<ChatMessage> _chat = new LinkedList<ChatMessage>();
        private readonly int _maxMessage = 100;


        private Chat()
        {
        }

        public static Chat Instance => _instance ?? (_instance = new Chat());

        public void Add(S_CHAT message)
        {
            Add(message.Username, message.Text);
        }

        public void Add(S_WHISPER message)
        {
            Add(message.Sender, message.Text);
        }

        private void Add(string sender, string message)
        {
            if (_chat.Count == _maxMessage)
            {
                _chat.RemoveFirst();
            }

            var rgx = new Regex("<[^>]+>");
            message = rgx.Replace(message, "");
            message = WebUtility.HtmlDecode(message);

            var chatMessage = new ChatMessage(sender, message);
            _chat.AddLast(chatMessage);
        }

        public List<ChatMessage> Get()
        {
            return _chat.ToList();
        }
    }
}