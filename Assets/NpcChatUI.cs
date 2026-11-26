using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI de chat (UI Toolkit) conectada ao NpcDialogue.
/// Requer um UIDocument no mesmo GameObject, com o UXML "NewUXMLTemplate".
/// Alem do chat, cria em codigo: badge colorido da intencao, linha de status
/// ("pensando...", "modo offline") e o botao "Encerrar" (memoria de longo prazo).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public sealed class NpcChatUI : MonoBehaviour
{
    /// true enquanto o jogador digita: o PlayerCapsuleController nao anda (WASD/espaco). Esc libera.
    public static bool IsTyping { get; private set; }

    [Header("Referências")]
    [SerializeField] private NpcDialogue dialogue;
    [SerializeField] private NpcCapsuleController npc;   // se vazio, procura na cena
    [SerializeField] private string npcDisplayName = "NPC";

    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement chatRoot;
    private VisualElement chatLog;
    private ScrollView chatScroll;
    private TextField chatInput;
    private Button sendButton;
    private Button endButton;
    private Label intentLabel;
    private Label statusLabel;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        root = uiDocument.rootVisualElement;

        chatRoot = root.Q<VisualElement>("chat-root");
        chatLog = root.Q<VisualElement>("chat-log");
        chatScroll = root.Q<ScrollView>("chat-scroll");
        chatInput = root.Q<TextField>("chat-input");
        sendButton = root.Q<Button>("chat-send-button");

        var nameLabel = root.Q<Label>("npc-name-label");
        if (nameLabel != null)
            nameLabel.text = npcDisplayName;

        BuildExtraWidgets();

        sendButton.clicked += OnSendClicked;
        chatInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
        chatInput.RegisterCallback<FocusInEvent>(_ => IsTyping = true);
        chatInput.RegisterCallback<FocusOutEvent>(_ => IsTyping = false);

        if (npc == null) npc = FindFirstObjectByType<NpcCapsuleController>();

        CloseChat();
    }

    private void OnEnable()
    {
        if (dialogue != null)
        {
            dialogue.BusyChanged += OnBusyChanged;
            dialogue.StatusChanged += OnStatusChanged;
        }
        if (npc != null) npc.Changed += ShowIntent;
    }

    private void OnDisable()
    {
        if (dialogue != null)
        {
            dialogue.BusyChanged -= OnBusyChanged;
            dialogue.StatusChanged -= OnStatusChanged;
        }
        if (npc != null) npc.Changed -= ShowIntent;
        IsTyping = false;
    }

    private void OnDestroy()
    {
        if (sendButton != null) sendButton.clicked -= OnSendClicked;
        if (endButton != null) endButton.clicked -= OnEndClicked;
    }

    // ------------------------------------------------------------------ widgets criados em codigo
    private void BuildExtraWidgets()
    {
        intentLabel = new Label("[~] NEUTRO");
        intentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        intentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        intentLabel.style.color = Color.white;
        intentLabel.style.fontSize = 14;
        intentLabel.style.paddingTop = intentLabel.style.paddingBottom = 4;
        intentLabel.style.marginBottom = 4;
        intentLabel.style.borderTopLeftRadius = intentLabel.style.borderTopRightRadius = 4;
        intentLabel.style.borderBottomLeftRadius = intentLabel.style.borderBottomRightRadius = 4;
        chatRoot.Insert(Mathf.Min(1, chatRoot.childCount), intentLabel);

        statusLabel = new Label("");
        statusLabel.style.color = new Color(1f, 0.85f, 0.3f);
        statusLabel.style.fontSize = 12;
        statusLabel.style.marginBottom = 4;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        chatRoot.Insert(Mathf.Max(0, chatRoot.childCount - 1), statusLabel);   // acima da linha do input

        var row = root.Q<VisualElement>("chat-input-row");
        if (row != null)
        {
            endButton = new Button { text = "Encerrar" };
            endButton.AddToClassList("chat-send-button");
            endButton.clicked += OnEndClicked;
            row.Add(endButton);
        }
        ShowIntent(NpcIntent.Neutro, "calmo");
    }

    // ------------------------------------------------------------------ feedback
    private void ShowIntent(NpcIntent intent, string mood)
    {
        if (intentLabel == null) return;
        string tag; Color c;
        switch (intent)
        {
            case NpcIntent.Alerta: tag = "[!] ALERTA"; c = new Color(0.85f, 0.15f, 0.15f); break;
            case NpcIntent.Missao: tag = "[>] MISSAO"; c = new Color(0.15f, 0.65f, 0.25f); break;
            default:               tag = "[~] NEUTRO"; c = new Color(0.20f, 0.45f, 0.85f); break;
        }
        intentLabel.text = tag + "   humor: " + mood;
        intentLabel.style.backgroundColor = c;
    }

    private void OnBusyChanged(bool busy)
    {
        sendButton.SetEnabled(!busy);
        if (endButton != null) endButton.SetEnabled(!busy);
    }

    private void OnStatusChanged(string text)
    {
        if (statusLabel != null) statusLabel.text = text;
    }

    // ------------------------------------------------------------------ chat
    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            OnSendClicked();
        else if (evt.keyCode == KeyCode.Escape)
            chatInput.Blur();                              // libera o jogador para andar
    }

    /// <summary>Abre a janela de chat. Chamado quando o NPC inicia uma conversa.</summary>
    public void OpenChat()
    {
        chatRoot.style.display = DisplayStyle.Flex;
        chatInput.Focus();
    }

    public void CloseChat()
    {
        chatRoot.style.display = DisplayStyle.None;
        IsTyping = false;
    }

    private void OnSendClicked()
    {
        string message = chatInput.value;
        if (string.IsNullOrWhiteSpace(message) || dialogue == null || dialogue.IsBusy)
            return;

        AddLine(message, isPlayer: true);
        chatInput.value = string.Empty;
        chatInput.Focus();

        dialogue.Ask(message);
    }

    private void OnEndClicked()
    {
        if (dialogue != null) dialogue.EndConversation();
    }

    /// <summary>Conectado ao evento onReply do NpcDialogue (UnityEvent&lt;string&gt;).</summary>
    public void OnNpcReply(string reply)
    {
        AddLine(reply, isPlayer: false);
    }

    private void AddLine(string text, bool isPlayer)
    {
        var label = new Label((isPlayer ? "Você: " : npcDisplayName + ": ") + text);
        label.AddToClassList("chat-line");
        label.AddToClassList(isPlayer ? "chat-line-player" : "chat-line-npc");
        chatLog.Add(label);

        chatScroll.schedule.Execute(() => chatScroll.scrollOffset = new Vector2(0f, float.MaxValue));
    }
}
