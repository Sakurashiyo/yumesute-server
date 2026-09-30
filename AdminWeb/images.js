"use strict";
function gameImage(kind, entry) {
  const frame = document.createElement("div"); frame.className = "game-image";
  const image = document.createElement("img");
  image.alt = entry.name; image.loading = "lazy"; image.decoding = "async"; image.width = 112; image.height = 112;
  image.src = "/admin/save-editor/images/" + kind + "/" + entry.id;
  image.onerror = () => { frame.replaceChildren(); const text = document.createElement("span"); text.textContent = "备份中暂无图片"; frame.append(text); frame.classList.add("missing"); };
  frame.append(image); return frame;
}
