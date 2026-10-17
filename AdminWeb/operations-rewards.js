"use strict";
const RewardEditor = (() => {
  let items, posters, pointId;
  const types = [[1, "道具"], [3, "海报"], [12, "金币"], [13, "免费宝石"]];
  function node(tag, text, className) {
    const element = document.createElement(tag);
    if (text !== undefined) element.textContent = text;
    if (className) element.className = className;
    return element;
  }
  function initialize(catalog) {
    items = new Map(catalog.items.map(item => [item.id, item]));
    posters = new Map(catalog.posters.map(poster => [poster.id, poster]));
    pointId = catalog.missionPass.pointItemId;
    for (const [id, entries] of [["reward-items", catalog.items], ["reward-posters", catalog.posters]]) {
      const list = document.getElementById(id);
      list.replaceChildren(...entries.map(entry => {
        const option = node("option"); option.value = entry.id; option.label = entry.name; return option;
      }));
    }
  }
  function create(reward, label, changed) {
    const wrapper = node("div", undefined, "reward-editor");
    const type = node("select"); type.setAttribute("aria-label", label + "类型");
    type.replaceChildren(...types.map(([value, text]) => {
      const option = node("option", text); option.value = value; return option;
    }));
    type.value = reward.type;
    const master = node("input"); master.type = "text"; master.inputMode = "numeric"; master.pattern = "[0-9]+";
    master.value = reward.masterId; master.required = true; master.setAttribute("aria-label", label + "主数据 ID");
    const quantity = node("input"); quantity.type = "number"; quantity.min = 1; quantity.max = 1000000;
    quantity.step = 1; quantity.required = true; quantity.value = reward.quantity; quantity.setAttribute("aria-label", label + "数量");
    const name = node("small", undefined, "reward-name");
    function render() {
      const money = reward.type === 12 || reward.type === 13;
      master.readOnly = money; master.setAttribute("list", reward.type === 3 ? "reward-posters" : "reward-items");
      const entry = (reward.type === 3 ? posters : items).get(reward.masterId);
      name.textContent = money ? (reward.type === 12 ? "金币" : "免费剧珠") : entry?.name || "请选择有效的主数据 ID";
      name.classList.toggle("invalid", !money && !entry);
      master.setCustomValidity(!money && (!entry || reward.type === 1 && reward.masterId === pointId)
        ? "请选择有效道具或海报；不能奖励通行证自身积分" : "");
      quantity.max = reward.type === 3 ? 5 : reward.type === 1 && entry ? Math.min(1000000, entry.maxQuantity) : 1000000;
    }
    type.onchange = () => {
      reward.type = Number(type.value);
      if (reward.type === 12 || reward.type === 13) { reward.masterId = 0; master.value = 0; }
      render(); changed();
    };
    master.oninput = () => { reward.masterId = Number(master.value); render(); changed(); };
    quantity.oninput = () => { reward.quantity = Number(quantity.value); changed(); };
    wrapper.append(type, master, quantity, name); render(); return wrapper;
  }
  return { initialize, create, node };
})();
