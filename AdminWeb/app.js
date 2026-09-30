"use strict";
const $ = id => document.getElementById(id);
const base = "/admin/save-editor/api";
let catalog, state, tab = "stats", busy = false, selection = 0, searchController;
let catalogPage = 0;
const labels = {rank:"玩家等级", exp:"当前等级经验", rankLimit:"等级上限", stamina:"体力", freeJewel:"免费剧珠", paidJewel:"付费剧珠", coin:"金币"};
const kinds = {card:"卡片", costume:"服装", item:"道具"};
function message(text, error = false) { $("message").textContent = text; $("message").className = error ? "error" : ""; }
async function api(path, options) {
  const response = await fetch(base + path, options);
  const value = await response.json();
  if (!response.ok) throw new Error(value.message || "请求失败");
  return value;
}
function node(tag, text, className) { const value = document.createElement(tag); value.textContent = text; if(className) value.className = className; return value; }
async function loadUsers() {
  searchController?.abort(); searchController = new AbortController();
  try {
    const users = await api("/users?q=" + encodeURIComponent($("search").value), {signal:searchController.signal});
    $("users").replaceChildren();
    for(const user of users) {
      const button = node("button", user.name, "user" + (state?.userId === user.id ? " selected" : ""));
      button.append(node("small", "ID " + user.publicId + " · RANK " + user.rank));
      button.onclick = () => { if(!busy) loadUser(user.id); }; $("users").append(button);
    }
    if(!users.length) $("users").append(node("p", "没有匹配的用户"));
  } catch(error) { if(error.name !== "AbortError") message(error.message, true); }
}
async function loadUser(id) {
  const token = ++selection;
  try { const value = await api("/users/" + id); if(token !== selection) return; state = value; message(""); render(); loadUsers(); }
  catch(error) { if(token === selection) message(error.message, true); }
}
function render() {
  $("empty").hidden = true; $("editor").hidden = false;
  $("name").textContent = state.name; $("identity").textContent = "玩家 ID " + state.publicId + " · 数据库 ID " + state.userId;
  $("summary").textContent = "RANK " + state.stats.rank + "  /  " + state.stats.rankLimit + "　 ·　 卡片 " + state.cards.length + "　 ·　 服装 " + state.costumes.length;
  $("fields").replaceChildren();
  for(const [key, label] of Object.entries(labels)) {
    const wrapper = node("label", label); const input = document.createElement("input");
    input.name = key; input.type = "number"; input.step = "1"; input.min = key === "rank" || key === "rankLimit" ? "1" : "0";
    input.max = key === "rank" || key === "rankLimit" ? "300" : "1000000000"; input.required = true; input.value = state.stats[key];
    wrapper.append(input); $("fields").append(wrapper);
  }
  setTab(tab);
}
function setTab(value) {
  tab = value; for(const button of document.querySelectorAll("[data-tab]")) button.classList.toggle("active", button.dataset.tab === tab);
  $("stats").hidden = tab !== "stats"; $("inventory").hidden = tab === "stats";
  if(tab === "stats") return;
  $("inventory-title").textContent = "管理持有" + kinds[tab];
  $("grant").textContent = tab === "item" ? "设置最终数量" : "添加持有";
  $("quantity").hidden = tab !== "item"; $("master").value = ""; $("masters").replaceChildren();
  for(const entry of catalog[tab + "s"]) { const option = document.createElement("option"); option.value = String(entry.id); option.label = entry.name; $("masters").append(option); }
  catalogPage = 0; $("catalog-search").value = ""; $("selection-preview").replaceChildren(); renderCatalog();
  renderOwned();
}
function selectEntry(entry) {
  if(busy) return;
  $("master").value = entry.id; $("selection-preview").replaceChildren(gameImage(tab, entry), node("span", entry.name + " · ID " + entry.id));
  if(tab === "item") { $("quantity").max = entry.maxQuantity; $("quantity").focus(); }
  renderCatalog();
}
function renderCatalog() {
  if(!catalog || tab === "stats") return;
  const query = $("catalog-search").value.toLowerCase();
  const entries = catalog[tab + "s"].filter(entry => (entry.name + entry.id).toLowerCase().includes(query));
  const pages = Math.max(1, Math.ceil(entries.length / 48)); catalogPage = Math.min(catalogPage, pages - 1);
  $("catalog-grid").replaceChildren();
  for(const entry of entries.slice(catalogPage * 48, (catalogPage + 1) * 48)) {
    const button = node("button", "", "catalog-tile" + (String(entry.id) === $("master").value ? " selected" : ""));
    button.disabled = busy; button.append(gameImage(tab, entry), node("span", entry.name), node("small", "ID " + entry.id));
    button.onclick = () => selectEntry(entry); $("catalog-grid").append(button);
  }
  if(!entries.length) $("catalog-grid").append(node("p", "没有符合条件的物品"));
  $("catalog-page").textContent = (catalogPage + 1) + " / " + pages + " · 共 " + entries.length + " 项";
  $("catalog-prev").disabled = busy || catalogPage === 0; $("catalog-next").disabled = busy || catalogPage >= pages - 1;
}
function renderOwned() {
  if(!state || tab === "stats") return;
  const masters = new Map(catalog[tab + "s"].map(entry => [entry.id, entry]));
  const query = $("owned-search").value.toLowerCase(); $("owned").replaceChildren();
  for(const owned of state[tab + "s"]) {
    const entry = masters.get(owned.masterId); const name = entry?.name || "未知主数据";
    if(!(name + owned.masterId).toLowerCase().includes(query)) continue;
    const row = node("article", "", "owned-tile"), title = node("div", name); title.append(node("small", "主数据 ID " + owned.masterId));
    row.append(gameImage(tab, entry || {id:owned.masterId, name}), title, node("p", tab === "card" ? "Lv." + owned.level + " · 觉醒 " + owned.awakening : tab === "item" ? "× " + owned.quantity : "已持有"));
    const actions = node("div", "", "tile-actions");
    if(tab === "item") { const edit = node("button", "编辑数量"); edit.onclick = () => { if(entry) selectEntry(entry); $("quantity").value = owned.quantity; $("quantity").focus(); }; actions.append(edit); }
    const remove = node("button", tab === "item" ? "清零" : "移除");
    remove.onclick = () => save({kind:tab, masterId:owned.masterId, remove:true}, "确认" + (tab === "item" ? "清零" : "移除") + "「" + name + "」？"); actions.append(remove); row.append(actions); $("owned").append(row);
  }
  if(!$("owned").children.length) $("owned").append(node("p", "暂无符合条件的持有物品"));
}
async function save(command, confirmation) {
  if(!state || busy || !confirm(confirmation)) return;
  busy = true; document.querySelectorAll("button").forEach(button => button.disabled = true);
  try { state = await api("/users/" + state.userId, {method:"POST", headers:{"Content-Type":"application/json"}, body:JSON.stringify({revision:state.revision, ...command})}); render(); message("已保存。请重新登录游戏以加载新存档。"); }
  catch(error) { message(error.message, true); }
  finally { busy = false; document.querySelectorAll("button").forEach(button => button.disabled = false); renderCatalog(); }
}
$("stats-form").onsubmit = event => { event.preventDefault(); const stats = Object.fromEntries([...new FormData(event.target)].map(([key, value]) => [key, Number(value)])); save({kind:"stats", stats}, "确认替换此玩家的等级、体力与货币？"); };
$("inventory-form").onsubmit = event => {
  event.preventDefault(); const masterId = Number($("master").value), entry = catalog[tab + "s"].find(value => value.id === masterId);
  if(!entry) { message("请从列表选择有效的主数据 ID", true); return; }
  save({kind:tab, masterId, quantity:Number($("quantity").value)}, "确认设置「" + entry.name + "」" + (tab === "item" ? "为 " + $("quantity").value + " 个" : "为已持有") + "？");
};
document.querySelectorAll("[data-tab]").forEach(button => button.onclick = () => setTab(button.dataset.tab));
$("owned-search").oninput = renderOwned;
$("catalog-search").oninput = () => { catalogPage = 0; renderCatalog(); };
$("catalog-prev").onclick = () => { catalogPage--; renderCatalog(); };
$("catalog-next").onclick = () => { catalogPage++; renderCatalog(); };
$("master").oninput = () => { const entry = tab === "stats" ? null : catalog[tab + "s"].find(value => String(value.id) === $("master").value); if(entry) selectEntry(entry); else $("selection-preview").replaceChildren(); };
$("refresh").onclick = () => { if(state && !busy && confirm("刷新将丢弃尚未保存的输入，继续？")) loadUser(state.userId); };
let debounce; $("search").oninput = () => { clearTimeout(debounce); debounce = setTimeout(loadUsers, 250); };
(async () => { try { catalog = await api("/catalog"); await loadUsers(); } catch(error) { message(error.message, true); } })();
