"use strict";
const $ = id => document.getElementById(id), node = RewardEditor.node;
const apiBase = "/admin/operations/api";
let catalog, snapshot, draft, busy = false, currentTab = "pass";
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
function message(text, error = false) { $("message").textContent = text; $("message").classList.toggle("error", error); }
async function api(path, options) {
  const response = await fetch(apiBase + path, options);
  const value = await response.json();
  if (!response.ok) throw new Error(value.message || "请求失败，请查看服务端日志");
  return value;
}
function isOpen(setting) {
  const now = Date.now();
  return setting.enabled && Date.parse(setting.startsAt) <= now && (!setting.endsAt || now < Date.parse(setting.endsAt));
}
function changed() {
  const dirty = snapshot && !same(draft, snapshot.configuration);
  $("save").disabled = busy || !dirty;
  $("reload").disabled = busy || !snapshot;
  $("dirty-dot").classList.toggle("dirty", dirty);
  $("save-state").textContent = busy ? "正在生成主数据并保存…" : dirty ? "有未保存的修改" : "配置已同步";
  if (!draft) return;
  $("pass-summary").textContent = isOpen(draft.missionPass) ? "开放中" : draft.missionPass.enabled ? "按时间开放" : "已关闭";
  $("gacha-summary").textContent = catalog.gachas.filter(pool => pool.available && draft.gachas[pool.id]).length + " / " + catalog.gachas.length + " 开放";
  $("bonus-summary").textContent = Object.values(draft.loginBonuses).filter(isOpen).length + " 项发放中";
}
function localInput(value) {
  if (!value) return "";
  const date = new Date(value), pad = n => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
function period(container, setting, label) {
  container.replaceChildren();
  for (const [key, title] of [["startsAt", "开始时间"], ["endsAt", "结束时间"]]) {
    const field = node("label", title, "field"), input = node("input");
    input.type = "datetime-local"; input.value = localInput(setting[key]); input.required = key === "startsAt";
    input.setAttribute("aria-label", label + title); input.step = 60;
    input.onchange = () => {
      if (input.value && !Number.isNaN(new Date(input.value).getTime())) setting[key] = new Date(input.value).toISOString();
      else if (key === "endsAt" && !input.value) setting[key] = null;
      changed();
    };
    field.append(input); container.append(field);
  }
  container.append(node("small", "结束留空为常驻 · " + Intl.DateTimeFormat().resolvedOptions().timeZone, "period-note"));
}
function toggle(title, description, checked, update) {
  const label = node("label", undefined, "toggle-card"), text = node("span");
  text.append(node("strong", title), node("small", description));
  const input = node("input"); input.type = "checkbox"; input.checked = checked; input.setAttribute("role", "switch");
  input.onchange = () => { update(input.checked); changed(); };
  label.append(text, input, node("span", undefined, "switch")); return label;
}
function setTab(tab) {
  if (!["pass","gachas","bonuses","notices"].includes(tab)) tab = "pass";
  if (currentTab !== tab) window.scrollTo({top: 0, behavior: "instant"});
  currentTab = tab;
  for (const button of document.querySelectorAll(".nav-item")) button.classList.toggle("active", button.dataset.tab === tab);
  for (const name of ["pass", "gachas", "bonuses", "notices"]) $("panel-" + name).hidden = name !== tab;
  document.querySelector(".save-bar").hidden = tab === "notices";
  if (location.hash !== "#" + tab) history.replaceState(null,"","#" + tab);
}
function renderStages() {
  const fragment = document.createDocumentFragment();
  for (const stage of draft.missionPass.stages) {
    const row = node("tr"); row.dataset.phase = stage.phase;
    const phase = node("td"), points = node("td");
    phase.append(node("span", String(stage.phase).padStart(2, "0"), "phase-number"));
    const input = node("input", undefined, "points"); input.type = "number"; input.min = 1; input.max = 20000;
    input.step = 1; input.required = true; input.value = stage.points; input.setAttribute("aria-label", `第 ${stage.phase} 阶积分门槛`);
    input.oninput = () => { stage.points = Number(input.value); changed(); };
    points.append(input); row.append(phase, points);
    for (const sp of [false, true]) {
      const cell = node("td"), reward = stage.rewards.find(value => value.sp === sp);
      cell.append(RewardEditor.create(reward, `第 ${stage.phase} 阶${sp ? "SP" : "普通"}奖励`, changed)); row.append(cell);
    }
    fragment.append(row);
  }
  $("stages").replaceChildren(fragment);
  $("loops").replaceChildren(...draft.missionPass.loopRewards.map((rewards, index) => {
    const card = node("article", undefined, "loop-card"); card.append(node("h4", `循环阶段 ${index + 1}`),
      RewardEditor.create(rewards[0], `循环阶段 ${index + 1} 奖励`, changed)); return card;
  }));
}
function render() {
  $("revision").textContent = "配置版本 " + snapshot.revision;
  $("pass-title").textContent = catalog.missionPass.name;
  $("pass-enabled").checked = draft.missionPass.enabled; $("pass-sp").checked = draft.missionPass.spEnabled;
  period($("pass-period"), draft.missionPass, "通行证");
  renderStages(); $("phase-filter").value = "";
  $("gachas").replaceChildren(...catalog.gachas.map((pool, index) => {
    const card = node("article", undefined, "gacha-card"), art = node("div", undefined, "gacha-art");
    art.append(node("strong", pool.type === 2 ? "ACTOR" : "POSTER"), node("span", pool.historical ? "REPLAY" : "ALL"));
    card.append(art, toggle(pool.name, "卡池 ID " + pool.id, draft.gachas[pool.id], value => { draft.gachas[pool.id] = value; }),
      node("div", pool.count + " 张收藏 · " + (pool.historical ? "往期收录名单 · 本服概率" : "全收录常驻") + (pool.available ? "" : " · 资源缺失，无法开启"), "gacha-description"));
    card.dataset.search = (pool.name + pool.id).toLowerCase();
    if (!pool.available && !draft.gachas[pool.id]) card.querySelector("input").disabled = true;
    return card;
  }));
  const items = new Map(catalog.items.map(item => [item.id, item.name]));
  $("bonuses").replaceChildren(...catalog.loginBonuses.map(bonus => {
    const setting = draft.loginBonuses[bonus.id], card = node("article", undefined, "bonus-card");
    card.append(toggle(bonus.title, "活动 ID " + bonus.id, setting.enabled, value => { setting.enabled = value; }));
    const description = node("p", undefined, "bonus-description");
    description.append(node("span", bonus.isLoop ? "循环签到" : "纪念活动", "tag"),
      document.createTextNode((bonus.thingType === 13 ? "免费剧珠" : items.get(bonus.masterId) || "道具 " + bonus.masterId)
        + " × " + bonus.quantity + " / 日 · " + bonus.days + " 天" + (bonus.isLoop ? "一轮" : "发满即止")));
    const dates = node("div", undefined, "period"); period(dates, setting, "活动 " + bonus.id);
    card.append(description, dates); return card;
  }));
  $("editor").disabled = busy; setTab(currentTab); changed();
  filterGachas();
}
async function load() {
  if (busy) return;
  busy = true; $("editor").disabled = true; $("save").disabled = true; $("reload").disabled = true;
  try {
    const values = await Promise.all([api("/catalog"), api("/config")]);
    catalog = values[0]; snapshot = values[1]; draft = structuredClone(snapshot.configuration);
    RewardEditor.initialize(catalog); message("");
  } catch (error) { message(error.message, true); }
  finally { busy = false; if (draft) render(); else { $("reload").disabled = false; $("save-state").textContent = "加载失败，请重试"; } }
}
$("pass-enabled").onchange = event => { draft.missionPass.enabled = event.target.checked; changed(); };
$("pass-sp").onchange = event => { draft.missionPass.spEnabled = event.target.checked; changed(); };
$("phase-filter").oninput = event => {
  const phase = Number(event.target.value);
  for (const row of $("stages").children) row.hidden = phase > 0 && Number(row.dataset.phase) !== phase;
};
document.querySelectorAll("[data-tab]").forEach(button => button.onclick = () => setTab(button.dataset.tab));
$("reload").onclick = load;
$("operations-form").noValidate = true;
$("operations-form").onsubmit = async event => {
  event.preventDefault(); if (busy || !draft) return;
  let previous = 0;
  for (const input of $("stages").querySelectorAll(".points")) {
    const value = Number(input.value);
    input.setCustomValidity(value <= previous ? "积分门槛必须严格递增" : "");
    previous = value;
  }
  const invalid = $("operations-form").querySelector("input:invalid,select:invalid");
  if (invalid) {
    const panel = invalid.closest(".panel");
    if (panel) setTab(panel.id.replace("panel-", ""));
    $("phase-filter").value = "";
    for (const row of $("stages").children) row.hidden = false;
    invalid.reportValidity(); return;
  }
  busy = true; $("editor").disabled = true; changed(); message("正在校验配置并生成客户端主数据…");
  try {
    snapshot = await api("/config", {method: "POST", headers: {"Content-Type": "application/json"},
      body: JSON.stringify({revision: snapshot.revision, configuration: draft})});
    draft = structuredClone(snapshot.configuration);
    message("已保存并应用。请重新登录游戏，同步新的活动与主数据。");
  } catch (error) { message(error.message, true); }
  finally { busy = false; render(); }
};
window.addEventListener("beforeunload", event => {
  if (snapshot && !same(draft, snapshot.configuration)) { event.preventDefault(); event.returnValue = ""; }
});
load();
currentTab = location.hash.slice(1) || "pass";
function filterGachas() {
  const query = $("gacha-search").value.trim().toLowerCase();
  for (const card of $("gachas").children) card.hidden = !card.dataset.search.includes(query);
}
$("gacha-search").oninput = filterGachas;
