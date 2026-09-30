using System.Text.Json;
using Npgsql;

static class AdminNotificationEndpoints
{
    public static void MapAdminNotificationEndpoints(this WebApplication app, LocalServerState state)
    {
        app.MapGet("/admin/notifications", context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            return context.Response.WriteAsync(AdminHtml);
        });

        app.MapGet("/admin/api/notifications", async context =>
        {
            await using var connection = await state.Database.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                select id,
                       title,
                       body,
                       banner_path,
                       posting_at,
                       last_updated_at,
                       starts_at,
                       ends_at,
                       notification_tab_category,
                       notification_category,
                       is_confirmation,
                       display_order
                from system_notifications
                order by display_order desc, posting_at desc, id desc
                """,
                connection);

            var rows = new List<NotificationAdminRow>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new NotificationAdminRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDateTime(4),
                    reader.GetDateTime(5),
                    reader.GetDateTime(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetBoolean(10),
                    reader.GetInt32(11)));
            }

            await context.Response.WriteAsJsonAsync(rows, JsonOptions);
        });

        app.MapPost("/admin/api/notifications", async context =>
        {
            var payload = await JsonSerializer.DeserializeAsync<NotificationAdminSaveRequest>(
                context.Request.Body,
                JsonOptions);
            if (payload is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid payload" }, JsonOptions);
                return;
            }

            var body = NormalizeBody(payload.Body);
            var postingAt = payload.PostingAt ?? DateTime.UtcNow;
            var lastUpdatedAt = payload.LastUpdatedAt ?? postingAt;
            var startsAt = payload.StartsAt ?? postingAt;

            await using var connection = await state.Database.OpenConnectionAsync();
            if (payload.Id is > 0)
            {
                await using var command = new NpgsqlCommand(
                    """
                    update system_notifications
                    set title = $2,
                        body = $3,
                        banner_path = $4,
                        posting_at = $5,
                        last_updated_at = $6,
                        starts_at = $7,
                        ends_at = $8,
                        notification_tab_category = $9,
                        notification_category = $10,
                        is_confirmation = $11,
                        display_order = $12,
                        updated_at = now()
                    where id = $1
                    returning id
                    """,
                    connection);
                AddUpdateParameters(command, payload, body, postingAt, lastUpdatedAt, startsAt);
                var id = await command.ExecuteScalarAsync();
                await context.Response.WriteAsJsonAsync(new { id }, JsonOptions);
            }
            else
            {
                await using var command = new NpgsqlCommand(
                    """
                    insert into system_notifications (
                      title,
                      body,
                      banner_path,
                      posting_at,
                      last_updated_at,
                      starts_at,
                      ends_at,
                      notification_tab_category,
                      notification_category,
                      is_confirmation,
                      display_order
                    )
                    values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
                    returning id
                    """,
                    connection);
                AddInsertParameters(command, payload, body, postingAt, lastUpdatedAt, startsAt);
                var id = await command.ExecuteScalarAsync();
                await context.Response.WriteAsJsonAsync(new { id }, JsonOptions);
            }
        });

        app.MapDelete("/admin/api/notifications/{id:long}", async (HttpContext context, long id) =>
        {
            await using var connection = await state.Database.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("delete from system_notifications where id = $1", connection);
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync();
            await context.Response.WriteAsJsonAsync(new { ok = true }, JsonOptions);
        });
    }

    static void AddUpdateParameters(
        NpgsqlCommand command,
        NotificationAdminSaveRequest payload,
        string body,
        DateTime postingAt,
        DateTime lastUpdatedAt,
        DateTime startsAt)
    {
        command.Parameters.AddWithValue(payload.Id ?? 0L);
        AddNotificationFields(command, payload, body, postingAt, lastUpdatedAt, startsAt);
    }

    static void AddInsertParameters(
        NpgsqlCommand command,
        NotificationAdminSaveRequest payload,
        string body,
        DateTime postingAt,
        DateTime lastUpdatedAt,
        DateTime startsAt)
    {
        AddNotificationFields(command, payload, body, postingAt, lastUpdatedAt, startsAt);
    }

    static void AddNotificationFields(
        NpgsqlCommand command,
        NotificationAdminSaveRequest payload,
        string body,
        DateTime postingAt,
        DateTime lastUpdatedAt,
        DateTime startsAt)
    {
        command.Parameters.AddWithValue(payload.Title?.Trim() is { Length: > 0 } title ? title : "Untitled");
        command.Parameters.AddWithValue(body);
        command.Parameters.AddWithValue(payload.BannerPath?.Trim() ?? "");
        command.Parameters.AddWithValue(postingAt);
        command.Parameters.AddWithValue(lastUpdatedAt);
        command.Parameters.AddWithValue(startsAt);
        command.Parameters.AddWithValue((object?)payload.EndsAt ?? DBNull.Value);
        command.Parameters.AddWithValue(payload.NotificationTabCategory);
        command.Parameters.AddWithValue(payload.NotificationCategory);
        command.Parameters.AddWithValue(payload.IsConfirmation);
        command.Parameters.AddWithValue(payload.DisplayOrder);
    }

    static string NormalizeBody(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0) return "[]";

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetRawText(),
                JsonValueKind.Object => JsonSerializer.Serialize(new[] { document.RootElement }),
                _ => "[]"
            };
        }
        catch
        {
            return JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object?>
                {
                    ["Element"] = 4,
                    ["Data"] = text
                }
            });
        }
    }

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    sealed record NotificationAdminRow(
        long Id,
        string Title,
        string Body,
        string BannerPath,
        DateTime PostingAt,
        DateTime LastUpdatedAt,
        DateTime StartsAt,
        DateTime? EndsAt,
        int NotificationTabCategory,
        int NotificationCategory,
        bool IsConfirmation,
        int DisplayOrder);

    sealed record NotificationAdminSaveRequest(
        long? Id,
        string? Title,
        string? Body,
        string? BannerPath,
        DateTime? PostingAt,
        DateTime? LastUpdatedAt,
        DateTime? StartsAt,
        DateTime? EndsAt,
        int NotificationTabCategory = 1,
        int NotificationCategory = 1,
        bool IsConfirmation = false,
        int DisplayOrder = 0);

    const string AdminHtml = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>System Notifications</title>
  <style>
    :root { color-scheme: light; --bg:#f6f7fb; --panel:#fff; --line:#dfe3ee; --text:#202436; --muted:#6b7280; --primary:#f45f75; --dark:#56596b; }
    * { box-sizing: border-box; }
    body { margin:0; font-family:"Segoe UI","Microsoft YaHei",sans-serif; background:var(--bg); color:var(--text); }
    header { height:56px; display:flex; align-items:center; justify-content:space-between; padding:0 22px; background:#2f3446; color:white; }
    header h1 { font-size:18px; margin:0; font-weight:650; }
    main { display:grid; grid-template-columns:360px 1fr; gap:16px; padding:16px; height:calc(100vh - 56px); }
    .panel { background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; min-height:0; }
    .toolbar { display:flex; gap:8px; padding:12px; border-bottom:1px solid var(--line); }
    input, textarea, select { width:100%; border:1px solid var(--line); border-radius:6px; padding:9px 10px; font:inherit; background:white; color:var(--text); }
    textarea { min-height:92px; resize:vertical; font-family:"Segoe UI","Microsoft YaHei",sans-serif; line-height:1.5; }
    button { border:0; border-radius:6px; padding:9px 12px; font:inherit; cursor:pointer; background:#e8ebf2; color:#202436; }
    button.primary { background:var(--primary); color:white; }
    button.danger { background:#43495d; color:white; }
    button.small { padding:6px 9px; font-size:12px; }
    .list { overflow:auto; height:calc(100% - 58px); }
    .item { padding:12px 14px; border-bottom:1px solid var(--line); cursor:pointer; }
    .item:hover, .item.active { background:#fff1f3; }
    .item-title { font-weight:650; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
    .item-meta { margin-top:5px; color:var(--muted); font-size:12px; display:flex; justify-content:space-between; gap:8px; }
    form { padding:16px; overflow:auto; height:100%; }
    .grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; }
    .field { display:flex; flex-direction:column; gap:6px; margin-bottom:12px; }
    label { font-size:12px; color:var(--muted); font-weight:650; }
    .hint { color:var(--muted); font-size:12px; line-height:1.5; }
    .status { color:#d9dce7; font-size:13px; }
    .block-toolbar { display:flex; flex-wrap:wrap; gap:8px; margin:8px 0 12px; }
    .block { border:1px solid var(--line); border-radius:8px; padding:12px; margin-bottom:10px; background:#fbfcff; }
    .block-head { display:grid; grid-template-columns:180px 1fr auto auto auto; gap:8px; align-items:center; margin-bottom:8px; }
    .preview { border-top:1px dashed var(--line); margin-top:8px; padding-top:8px; color:#56596b; white-space:pre-wrap; line-height:1.65; }
    .preview-title { background:var(--dark); color:white; text-align:center; border-radius:4px; padding:5px 10px; font-weight:700; }
    .preview-note { color:#ee5f5f; font-weight:700; }
    details { margin-top:8px; }
    #rawBody { min-height:120px; font-family:Consolas,monospace; font-size:12px; }
    .actions { display:flex; gap:10px; position:sticky; bottom:-16px; background:linear-gradient(transparent,white 24px,white); padding-top:28px; }
  </style>
</head>
<body>
  <header>
    <h1>system_notifications 编辑器</h1>
    <div class="status" id="status">加载中</div>
  </header>
  <main>
    <section class="panel">
      <div class="toolbar">
        <input id="search" placeholder="搜索标题 / ID">
        <button class="primary" id="newBtn" type="button">新建</button>
      </div>
      <div class="list" id="list"></div>
    </section>
    <section class="panel">
      <form id="form">
        <input type="hidden" id="id">
        <div class="grid">
          <div class="field">
            <label>标题 title</label>
            <input id="title" required>
          </div>
          <div class="field">
            <label>banner_path</label>
            <input id="bannerPath" placeholder="Home/common_1 / Notification/Banner01660">
          </div>
          <div class="field">
            <label>Tab 分类</label>
            <select id="notificationTabCategory">
              <option value="1">1 Important</option>
              <option value="2">2 UpdateInformation</option>
              <option value="3">3 BugInformation</option>
            </select>
          </div>
          <div class="field">
            <label>通知分类</label>
            <select id="notificationCategory">
              <option value="1">1 Notification</option>
              <option value="2">2 Update</option>
              <option value="3">3 Campaign</option>
              <option value="4">4 Event</option>
              <option value="5">5 Gacha</option>
              <option value="6">6 Bug</option>
            </select>
          </div>
          <div class="field">
            <label>posting_at</label>
            <input id="postingAt" type="datetime-local">
          </div>
          <div class="field">
            <label>last_updated_at</label>
            <input id="lastUpdatedAt" type="datetime-local">
          </div>
          <div class="field">
            <label>starts_at</label>
            <input id="startsAt" type="datetime-local">
          </div>
          <div class="field">
            <label>ends_at</label>
            <input id="endsAt" type="datetime-local">
          </div>
          <div class="field">
            <label>display_order</label>
            <input id="displayOrder" type="number" value="0">
          </div>
        </div>
        <label style="display:flex;align-items:center;gap:8px;margin:4px 0 14px;">
          <input id="isConfirmation" type="checkbox" style="width:auto"> is_confirmation
        </label>
        <div class="field">
          <label>正文块 body blocks</label>
          <div class="hint">官方格式：Element=2 大标题横条，Element=3 注意事项标题，Element=4 普通富文本，Element=5 结尾文本，Element=6 图片/信息资源。正文支持 &lt;color=#ee5f5f&gt;红字&lt;/color&gt; 和 &lt;size=33%&gt;大字&lt;/size&gt;。</div>
          <div class="block-toolbar">
            <button type="button" class="small" onclick="addBlock(2,'服务終了までのスケジュール')">加大标题</button>
            <button type="button" class="small" onclick="addBlock(3,'注意事項')">加注意标题</button>
            <button type="button" class="small" onclick="addBlock(4,'本文を入力してください')">加正文</button>
            <button type="button" class="small" onclick="addRedDateBlock()">加红色日期</button>
            <button type="button" class="small" onclick="addBlock(6,'Information/0')">加图片/信息</button>
            <button type="button" class="small" onclick="loadOfficialSample()">官方风格示例</button>
          </div>
          <div id="blocks"></div>
          <details>
            <summary>查看 / 粘贴原始 JSON</summary>
            <textarea id="rawBody"></textarea>
            <div class="block-toolbar">
              <button type="button" class="small" onclick="applyRawBody()">从 JSON 应用到块</button>
              <button type="button" class="small" onclick="syncRawBody()">从块刷新 JSON</button>
            </div>
          </details>
        </div>
        <div class="actions">
          <button class="primary" type="submit">保存</button>
          <button class="danger" id="deleteBtn" type="button">删除</button>
          <button id="reloadBtn" type="button">刷新</button>
        </div>
      </form>
    </section>
  </main>
  <script>
    let rows = [];
    let blocks = [];
    let currentId = null;
    const $ = id => document.getElementById(id);
    const status = text => $("status").textContent = text;
    const toLocalInput = value => value ? new Date(value).toISOString().slice(0, 16) : "";
    const fromLocalInput = value => value ? new Date(value).toISOString() : null;

    async function load() {
      status("加载中");
      rows = await fetch("/admin/api/notifications").then(r => r.json());
      renderList();
      status(`${rows.length} 条通知`);
      if (!currentId && rows[0]) edit(rows[0].id);
    }

    function renderList() {
      const q = $("search").value.trim().toLowerCase();
      $("list").innerHTML = "";
      rows.filter(row => !q || String(row.id).includes(q) || row.title.toLowerCase().includes(q)).forEach(row => {
        const div = document.createElement("div");
        div.className = "item" + (row.id === currentId ? " active" : "");
        div.onclick = () => edit(row.id);
        div.innerHTML = `<div class="item-title">#${row.id} ${escapeHtml(row.title)}</div>
          <div class="item-meta"><span>tab ${row.notificationTabCategory} / cat ${row.notificationCategory}</span><span>${new Date(row.postingAt).toLocaleString()}</span></div>`;
        $("list").appendChild(div);
      });
    }

    function edit(id) {
      const row = rows.find(x => x.id === id);
      if (!row) return;
      currentId = id;
      $("id").value = row.id;
      $("title").value = row.title;
      $("bannerPath").value = row.bannerPath || "";
      $("postingAt").value = toLocalInput(row.postingAt);
      $("lastUpdatedAt").value = toLocalInput(row.lastUpdatedAt);
      $("startsAt").value = toLocalInput(row.startsAt);
      $("endsAt").value = toLocalInput(row.endsAt);
      $("notificationTabCategory").value = row.notificationTabCategory;
      $("notificationCategory").value = row.notificationCategory;
      $("isConfirmation").checked = row.isConfirmation;
      $("displayOrder").value = row.displayOrder;
      blocks = parseBlocks(row.body);
      renderBlocks();
      renderList();
    }

    function clearForm() {
      currentId = null;
      $("form").reset();
      $("id").value = "";
      const now = new Date().toISOString().slice(0, 16);
      $("postingAt").value = now;
      $("lastUpdatedAt").value = now;
      $("startsAt").value = now;
      $("notificationTabCategory").value = "1";
      $("notificationCategory").value = "1";
      blocks = [{ Element: 4, Data: "本文を入力してください" }];
      renderBlocks();
      renderList();
    }

    function parseBlocks(value) {
      try {
        const parsed = JSON.parse(value || "[]");
        const list = Array.isArray(parsed) ? parsed : [parsed];
        return list.map(x => ({ Element: Number(x.Element ?? x.element ?? 4), Data: String(x.Data ?? x.data ?? "") }));
      } catch {
        return value ? [{ Element: 4, Data: value }] : [];
      }
    }

    function renderBlocks() {
      $("blocks").innerHTML = "";
      blocks.forEach((block, index) => {
        const div = document.createElement("div");
        div.className = "block";
        div.innerHTML = `
          <div class="block-head">
            <select onchange="setBlockElement(${index}, this.value)">
              ${option(2, "2 大标题", block.Element)}
              ${option(3, "3 注意标题", block.Element)}
              ${option(4, "4 正文富文本", block.Element)}
              ${option(5, "5 结尾文本", block.Element)}
              ${option(6, "6 图片/信息", block.Element)}
            </select>
            <span class="hint">${elementHint(block.Element)}</span>
            <button type="button" class="small" onclick="moveBlock(${index}, -1)">上移</button>
            <button type="button" class="small" onclick="moveBlock(${index}, 1)">下移</button>
            <button type="button" class="small danger" onclick="deleteBlock(${index})">删除</button>
          </div>
          <textarea oninput="setBlockData(${index}, this.value)">${escapeHtml(block.Data)}</textarea>
          <div id="preview-${index}" class="preview ${block.Element === 2 ? "preview-title" : block.Element === 3 ? "preview-note" : ""}">${previewBlock(block)}</div>`;
        $("blocks").appendChild(div);
      });
      syncRawBody();
    }

    function option(value, text, selected) {
      return `<option value="${value}" ${Number(selected) === value ? "selected" : ""}>${text}</option>`;
    }

    function elementHint(element) {
      return {
        2: "截图中的深色横条标题",
        3: "红色/注意事项类标题",
        4: "支持 color/size 标签的正文",
        5: "官方常用于结尾问候文本",
        6: "资源路径，例如 Information/0"
      }[element] || "";
    }

    function previewBlock(block) {
      if (block.Element === 6) return escapeHtml(`[资源] ${block.Data}`);
      return renderRichText(block.Data);
    }

    function renderRichText(value) {
      let html = escapeHtml(value);
      html = html.replace(/&lt;color=(#[0-9a-fA-F]{6})&gt;([\s\S]*?)&lt;\/color&gt;/g, '<span style="color:$1;font-weight:700">$2</span>');
      html = html.replace(/&lt;size=([0-9]+)%&gt;([\s\S]*?)&lt;\/size&gt;/g, '<span style="font-size:$1%">$2</span>');
      return html;
    }

    function setBlockElement(index, value) {
      blocks[index].Element = Number(value);
      renderBlocks();
    }

    function setBlockData(index, value) {
      blocks[index].Data = value;
      const preview = $("preview-" + index);
      if (preview) preview.innerHTML = previewBlock(blocks[index]);
      syncRawBody();
    }

    function addBlock(element, data) {
      blocks.push({ Element: element, Data: data });
      renderBlocks();
    }

    function addRedDateBlock() {
      addBlock(4, "<color=#ee5f5f>* 2026年7月17日(金) 15:00</color>\n　・内容を入力してください");
    }

    function moveBlock(index, delta) {
      const next = index + delta;
      if (next < 0 || next >= blocks.length) return;
      [blocks[index], blocks[next]] = [blocks[next], blocks[index]];
      renderBlocks();
    }

    function deleteBlock(index) {
      blocks.splice(index, 1);
      renderBlocks();
    }

    function loadOfficialSample() {
      blocks = [
        { Element: 4, Data: "<size=33%>いつも『ワールドダイスター 夢のステラリウム』をご利用いただき、誠にありがとうございます。</size>" },
        { Element: 2, Data: "サービス終了までのスケジュール" },
        { Element: 4, Data: "<color=#ee5f5f>* 2026年7月17日(金) 15:00</color>\n　・「劇ジュエル(有償)」の販売停止\n\n<color=#ee5f5f>* 2026年9月29日(火) 14:00</color>\n　・サービス終了" },
        { Element: 3, Data: "注意事項" },
        { Element: 4, Data: "・ここに注意事項を入力してください。" },
        { Element: 5, Data: "最後までよろしくお願いいたします。" }
      ];
      renderBlocks();
    }

    function syncRawBody() {
      $("rawBody").value = JSON.stringify(blocks, null, 2);
    }

    function applyRawBody() {
      blocks = parseBlocks($("rawBody").value);
      renderBlocks();
    }

    $("form").onsubmit = async event => {
      event.preventDefault();
      syncRawBody();
      const payload = {
        id: $("id").value ? Number($("id").value) : null,
        title: $("title").value,
        body: $("rawBody").value,
        bannerPath: $("bannerPath").value,
        postingAt: fromLocalInput($("postingAt").value),
        lastUpdatedAt: fromLocalInput($("lastUpdatedAt").value),
        startsAt: fromLocalInput($("startsAt").value),
        endsAt: fromLocalInput($("endsAt").value),
        notificationTabCategory: Number($("notificationTabCategory").value),
        notificationCategory: Number($("notificationCategory").value),
        isConfirmation: $("isConfirmation").checked,
        displayOrder: Number($("displayOrder").value || 0)
      };
      status("保存中");
      const result = await fetch("/admin/api/notifications", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      }).then(r => r.json());
      currentId = Number(result.id || payload.id);
      await load();
      status("已保存");
    };

    $("deleteBtn").onclick = async () => {
      if (!currentId || !confirm("删除这条通知？")) return;
      await fetch(`/admin/api/notifications/${currentId}`, { method: "DELETE" });
      currentId = null;
      await load();
      clearForm();
      status("已删除");
    };

    $("newBtn").onclick = clearForm;
    $("reloadBtn").onclick = load;
    $("search").oninput = renderList;

    function escapeHtml(value) {
      return String(value).replace(/[&<>"']/g, ch => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;" }[ch]));
    }

    load();
  </script>
</body>
</html>
""";
}
