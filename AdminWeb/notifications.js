"use strict";
(() => {
  let rows = [], banners = [], editing, blocks = [], saving = false, dirty = false;
  const status = text => { $("notice-heading").textContent = text; };
  function mayLeave() { return !dirty || window.confirm("这条公告有未保存的修改，是否放弃修改？"); }
  function markDirty() { dirty = true; }
  function renderList() {
    const query = $("notice-search").value.trim().toLowerCase();
    $("notice-list").replaceChildren(...rows.filter(row => (row.title + row.id).toLowerCase().includes(query)).map(row => {
      const button = node("button", undefined, "notice-list-item"); button.type = "button";
      const now = Date.now(), active = Date.parse(row.startsAt) <= now && (!row.endsAt || Date.parse(row.endsAt) > now);
      button.append(node("strong", row.title), node("small", `#${row.id} · ${active ? "展示中" : "未展示"} · 版本 ${row.revision}`));
      button.onclick = () => { if (mayLeave()) select(row); }; return button;
    }));
  }
  function bannerOptions() {
    const query = $("banner-search").value.trim().toLowerCase(), selected = editing?.bannerPath || "";
    const matches = banners.filter(banner => (banner.name + banner.path).toLowerCase().includes(query));
    const options = [new Option("请选择游戏中存在的横幅", "")];
    for (const banner of banners)
      if (banner.path === selected || (banner.name + banner.path).toLowerCase().includes(query))
        options.push(new Option(`${banner.name} · ${banner.path}`, banner.path));
    $("notice-banner").replaceChildren(...options); $("notice-banner").value = selected;
    $("banner-count").textContent = matches.length ? `点击图片选择 · ${matches.length} / ${banners.length} 张横幅` : "没有匹配的横幅，请换个名称或路径搜索";
    $("banner-gallery").replaceChildren(...matches.map(banner => {
      const button = node("button",undefined,"banner-tile"); button.type = "button";
      button.dataset.path = banner.path; button.setAttribute("aria-label",`选择横幅 ${banner.name} ${banner.path}`);
      const image = node("img"); image.src = `${apiBase}/notification-banners/${banner.key}`;
      image.alt = banner.name; image.loading = "lazy"; image.decoding = "async";
      button.append(image,node("strong",banner.name),node("small",banner.path));
      button.onclick = () => chooseBanner(banner.path); return button;
    }));
    previewBanner();
  }
  function chooseBanner(path) {
    editing.bannerPath = path; $("notice-banner").value = path;
    previewBanner(); markDirty();
  }
  function previewBanner() {
    const banner = banners.find(banner => banner.path === $("notice-banner").value);
    for (const tile of $("banner-gallery").children)
      tile.setAttribute("aria-pressed",String(tile.dataset.path === banner?.path));
    $("notice-banner-preview").hidden = !banner;
    if (banner) $("notice-banner-preview").src = `${apiBase}/notification-banners/${banner.key}`;
    else $("notice-banner-preview").removeAttribute("src");
    $("banner-path").textContent = banner ? "游戏资源：Textures/Banners/" + banner.path + ".astc.gz"
      : editing?.bannerPath ? `原 banner ${editing.bannerPath} 在本地缺失，请选择可用横幅后保存。` : `${banners.length} 张本地游戏横幅可供选择`;
  }
  function renderBlocks() {
    $("notice-blocks").replaceChildren(...blocks.map((block, index) => {
      const card = node("article", undefined, "notice-block"), tools = node("div", undefined, "notice-block-tools");
      const select = node("select");
      for (const [type, label] of [[2,"章节标题"],[3,"注意事项标题"],[4,"正文（支持游戏富文本）"],[5,"结束语"],[6,"游戏 Information 图片路径"]])
        select.append(new Option(label, type));
      select.value = block.Element;
      select.onchange = () => { block.Element = Number(select.value); markDirty(); };
      tools.append(select);
      for (const [label, action] of [
        ["上移", () => { if (index > 0) [blocks[index-1],blocks[index]] = [blocks[index],blocks[index-1]]; }],
        ["下移", () => { if (index+1 < blocks.length) [blocks[index+1],blocks[index]] = [blocks[index],blocks[index+1]]; }],
        ["移除", () => { blocks.splice(index,1); }]
      ]) {
        const button = node("button", label); button.type = "button";
        button.onclick = () => { action(); markDirty(); renderBlocks(); }; tools.append(button);
      }
      const text = node("textarea"); text.value = block.Data; text.maxLength = 12000; text.rows = 4;
      text.setAttribute("aria-label", `公告内容块 ${index+1}`);
      text.oninput = () => { block.Data = text.value; markDirty(); };
      card.append(tools,text); return card;
    }));
  }
  function select(row) {
    editing = structuredClone(row); dirty = false;
    try { blocks = JSON.parse(row.body); }
    catch { blocks = [{Element:4,Data:row.body}]; }
    if (!Array.isArray(blocks) || blocks.some(block => typeof block?.Data !== "string" || !Number.isInteger(block.Element)))
      blocks = [{Element:4,Data:row.body}];
    $("notice-editor").disabled = false;
    status(row.id ? `编辑公告 #${row.id} · 版本 ${row.revision}` : "新建公告");
    for (const [id,key] of [["notice-title","title"],["notice-tab","notificationTabCategory"],["notice-category","notificationCategory"],["notice-order","displayOrder"]])
      $(id).value = row[key];
    $("notice-posting").value = localInput(row.postingAt); $("notice-updated").value = localInput(row.lastUpdatedAt);
    $("notice-confirmation").checked = row.isConfirmation;
    period($("notice-period"),editing,"公告");
    $("banner-search").value = ""; bannerOptions(); renderBlocks();
  }
  async function loadNotices(keepId = editing?.id) {
    const values = await Promise.all([api("/notifications"),api("/notification-banners")]);
    rows = values[0]; banners = values[1]; renderList();
    const row = rows.find(row => row.id === keepId);
    if (row) select(row);
    else if (!editing && rows.length) select(rows[0]);
    else if (!editing) newNotice();
  }
  function newNotice() {
    const now = new Date().toISOString();
    select({id:0,revision:0,title:"",body:'[{"Element":4,"Data":""}]',bannerPath:"",
      postingAt:now,lastUpdatedAt:now,startsAt:now,endsAt:null,
      notificationTabCategory:1,notificationCategory:1,isConfirmation:false,displayOrder:0});
  }
  $("notice-form").addEventListener("input",event => { if (event.target.id !== "banner-search") markDirty(); });
  $("notice-new").onclick = () => { if (!saving && mayLeave()) newNotice(); };
  $("notice-reload").onclick = async () => {
    if (saving || !mayLeave()) return;
    try { await loadNotices(); message("公告已重新读取"); } catch (error) { message(error.message,true); }
  };
  $("notice-search").oninput = renderList;
  $("banner-search").oninput = bannerOptions;
  $("notice-banner").onchange = () => chooseBanner($("notice-banner").value);
  $("notice-add-block").onclick = () => { if (blocks.length < 100) { blocks.push({Element:4,Data:""}); markDirty(); renderBlocks(); } };
  $("notice-form").onsubmit = async event => {
    event.preventDefault(); if (saving || !editing) return;
    const request = {...editing,title:$("notice-title").value,body:JSON.stringify(blocks),
      notificationTabCategory:Number($("notice-tab").value),notificationCategory:Number($("notice-category").value),
      displayOrder:Number($("notice-order").value),isConfirmation:$("notice-confirmation").checked,
      postingAt:new Date($("notice-posting").value).toISOString(),lastUpdatedAt:new Date($("notice-updated").value).toISOString(),
      bannerPath:$("notice-banner").value};
    saving = true; $("notice-editor").disabled = true;
    let savedAlready = false;
    try {
      const saved = await api("/notifications",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(request)});
      editing = {...request,id:saved.id,revision:request.id === 0 ? 0 : request.revision + 1};
      dirty = false; savedAlready = true;
      await loadNotices(saved.id); message("公告已保存并应用，重新打开游戏公告即可查看。");
    } catch (error) { message(savedAlready ? "公告已保存，但重新读取失败。请点击重新读取公告后继续编辑。" : error.message,true); }
    finally { saving = false; $("notice-editor").disabled = false; }
  };
  window.addEventListener("beforeunload",event => { if (dirty) { event.preventDefault(); event.returnValue = ""; } });
  loadNotices().catch(error => message(error.message,true));
})();
