(function app(){
"use strict";
const data = globalThis.MDSM_WIKI;
const blocks = new Map(data.blocks.map(block => [block.id, block]));
const items = new Map(data.items.map(item => [item.id, item]));
const placeable = data.items.filter(item => item.block === null);
const params = new URLSearchParams(location.search);
const main = document.querySelector("main");
const html = (value) => String(value).replace(/[&<>"']/g, c => ({"&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;"}[c]));
const number = (n) => new Intl.NumberFormat("en").format(n);
const blockUrl = (id) => `entry.html?block=${id}`;
const itemUrl = (item) => item.block === null ? `entry.html?item=${item.id}` : blockUrl(item.block);
const blockLink = (id) => `<a href="${blockUrl(id)}">${html(blocks.get(id).name)}</a>`;
const itemLink = (id) => `<a href="${itemUrl(items.get(id))}">${html(items.get(id).name)}</a>`;
const sprite = (entry, kind = "item") => {
  const block = kind === "block" ? entry : blocks.get(entry.block);
  if (block?.key.startsWith("Water")) return '<span aria-hidden="true" class="sprite water"></span>';
  const useItems = kind === "item" && entry.sprite >= 0;
  const tile = useItems ? entry.sprite : block?.tile ?? -1;
  if (tile < 0 || tile === 85 && block?.key === "Peony" || tile === 86 && block?.key === "Violet") return '<span aria-hidden="true" class="sprite placeholder"></span>';
  const dye = block?.tint;
  return `<span aria-hidden="true" class="sprite${dye ? " tint" : ""}" style="--atlas:url(assets/${useItems ? "items" : "terrain"}.png);--col:${tile % 16};--row:${Math.floor(tile / 16)}${dye ? `;--dye:${dye}` : ""}"></span>`;
};
const station = (r) => r.station === "Mortar" ? "Mortar and Pestle" : r.station === "Metalworking" ? "Metalworking Bench" : r.width <= 2 && r.height <= 2 ? "Inventory or Crafting Table" : "Crafting Table";
function describeBlock(block) {
  if (blockNotes[block.key]) return blockNotes[block.key];
  if (block.key.startsWith("Water")) return block.key === "WaterSource" ? "A persistent water source. Scoop it with a bucket or place it with a water bucket. Moving water can push entities." : "A simulation state for flowing or falling water. It can move entities and hydrate nearby farmland. Buckets collect source blocks.";
  if (block.key.startsWith("Lava")) return block.key === "LavaSource" ? "A persistent lava source. Scoop or place it with a bucket. Water meeting a source forms obsidian." : "A simulation state for flowing or falling lava. Water meeting flowing lava forms cobblestone.";
  if (block.key.startsWith("Stained")) return block.shape === "Pane"
    ? "A tinted, solid pane that connects to nearby panes and full solid blocks. Craft it from matching stained blocks or dye clear panes. Ordinary mining drops nothing."
    : "A tinted glass block made by surrounding dye with clear glass. It can be crafted into matching panes. Ordinary mining drops nothing.";
  if (block.stage) return `Growth stage ${block.stage} of 7. This crop grows on hydrated farmland. ${block.stage === 7 ? "Harvest it for mature produce." : "Wait for maturity for a better harvest."}`;
  if (/Flower|Cornflower|Peony|Violet|Daisy|Marigold/.test(block.key)) {
    const grinding = data.recipes.find(r => r.station === "Mortar" && r.cells.some(c => c.id === block.id));
    const season = /Peony|Violet/.test(block.key) ? " It generates in temperate terrain during spring." : " It can generate in temperate flower patches.";
    return `A non-solid plant that drops itself when broken and can be replanted on grass.${season}${grinding ? ` Grind it for ${items.get(grinding.output).name.toLowerCase()}.` : ""}`;
  }
  return `A ${block.solid ? "solid" : "non-solid"} ${block.shape.toLowerCase()} block. See the properties and linked recipes for its current behavior.`;
}
function describeItem(item) {
  if (ordinaryItemNotes[item.name]) return ordinaryItemNotes[item.name];
  if (item.armor !== "None") return `Equip in the ${item.armor.toLowerCase()} slot. Its ${item.armorPoints} armor points reduce incoming damage. Right-click while holding it to equip or swap it.`;
  const toolUses = { Pickaxe:"Mine stone and ores. The tool tier determines which minerals can be recovered.", Axe:"Break wooden blocks faster.", Sword:"Deal melee damage to mobs.", Shovel:"Dig earth and collect snowballs from snow layers.", Hoe:"Right-click dirt or grass to till farmland." };
  if (toolUses[item.tool]) return toolUses[item.tool];
  if (item.name.endsWith(" Dye")) return "Use this pigment to color glass blocks or panes in a crafting table. Some colors can be mixed in the mortar and pestle.";
  return "An inventory material. Its associated recipes are listed below.";
}
const tag = (str) => `<span class="tag">${html(str)}</span>`;
const ingredientSummary = (r) => {
  const totals = new Map();
  r.cells.filter(c => c.id && c.count).forEach(c => totals.set(c.id, (totals.get(c.id) || 0) + c.count));
  return [...totals].map(([id, count]) => `${count} × ${itemLink(id)}`).join(", ");
};
function recipeCard(r) {
  const output = items.get(r.output);
  const pattern = r.shapeless ? r.cells.filter(c => c.id) : r.cells;
  return `<article class="recipe" id="craft-${r.id}">
    <div class="recipe-title">${sprite(output)}<h3>${itemLink(output.id)} × ${r.count}</h3></div>
    <div class="tags">${tag(station(r))}${tag(r.shapeless ? "Shapeless" : `${r.width} × ${r.height} pattern`)}</div>
    <div class="pattern" style="--width:${r.shapeless ? pattern.length : r.width}" aria-label="${r.shapeless ? "Ingredients in any order" : "Recipe grid, left to right by row"}">
      ${pattern.map(c => c.id ? `<a class="slot" href="${itemUrl(items.get(c.id))}" title="${html(items.get(c.id).name)} × ${c.count}" aria-label="${html(items.get(c.id).name)} × ${c.count}">${sprite(items.get(c.id))}${c.count>1?`<b>${c.count}</b>`:""}</a>` : '<span class="slot empty" aria-label="Empty slot"></span>').join("")}
    </div>
    <p class="ingredients">${ingredientSummary(r)}</p>
  </article>`;
}
function smeltRows(recipes) {
  return recipes.map(r => `<tr><td>${sprite(items.get(r.input))} ${itemLink(r.input)}</td><td>${sprite(items.get(r.output))} ${itemLink(r.output)}</td><td>${r.machines.map(m => `${blockLink(m.block)} · ${m.seconds}s`).join("<br>")}</td></tr>`).join("");
}
function smeltTable(recipes) {
  return `<div class="table-scroll"><table class="table"><thead><tr><th>Input</th><th>Output</th><th>Machine and time per item</th></tr></thead><tbody>${smeltRows(recipes)}</tbody></table></div>`;
}
function catalog() {
  const list = document.querySelector("#catalog");
  const query = document.querySelector("#catalog-query");
  const type = document.querySelector("#catalog-kind");
  const states = document.querySelector("#catalog-states");
  const pagination = document.querySelector("#pages");
  if (["blocks","items"].includes(params.get("kind"))) type.value = params.get("kind");
  query.value = params.get("q") || "";
  let page = 1;
  const size = 36;
  function render() {
    const q = normalized(query.value);
    const blockResults = type.value === "items" ? [] : data.blocks.filter(b => (q || states.checked || b.placeable || ["Air","WaterSource","LavaSource","FarmlandDry","FarmlandWet","Snow","Fire"].includes(b.key) || b.stage === 7) && matches(b,q)).map(info => ({info,kind:"block"}));
    const itemResults = type.value === "blocks" ? [] : placeable.filter(i=>matches(i,q)).map(info=>({info,kind:"item"}));
    const merged = [...blockResults,...itemResults].sort((a,b)=>a.info.name.localeCompare(b.info.name));
    const last = Math.ceil(merged.length / size);
    page = Math.max(1, Math.min(page,last));
    document.querySelector("#catalog-count").textContent = `${merged.length} ${merged.length === 1 ? "entry" : "entries"}${last > 1 ? ` · Page ${page} of ${last}` : ""}`;
    list.innerHTML = merged.slice((page-1)*size,page*size).map(({info,kind})=>`<a class="card" href="${kind==="block"?blockUrl(info.id):itemUrl(info)}">${sprite(info,kind)}<span><strong>${html(info.name)}</strong><small>${kind==="block"?"Block":"Item"} ID ${info.id}${kind==="item"&&info.tool!=="None"?` · ${html(info.tool)}`:""}</small></span></a>`).join("") || '<p>No entries match this search.</p>';
    pagination.innerHTML = Array.from({length:last},(_,i)=>last===1?"":`<button type="button" data-page="${i+1}" aria-label="Page ${i+1}"${page===i+1?' aria-current="page"':""}>${i+1}</button>`).join("");
  }
  [query,type,states].forEach(control=>control.addEventListener("input",()=>{page=1;render();}));
  pagination.addEventListener("click",event=>{
    const button=event.target.closest("[data-page]");
    if(button) {page=Number(button.dataset.page);render(); document.querySelector("#catalog-count").focus();}
  });
  render();
}
function recipes() {
  const search = document.querySelector("#recipe-query");
  const filter = document.querySelector("#recipe-station");
  const result = document.querySelector("#craft-results");
  const smelt = document.querySelector("#smelt-results");
  search.value = params.get("q") || "";
  function render() {
    const value = filter.value;
    const q = normalized(search.value);
    const shown = data.recipes.filter(r =>
      (value === "all" || value === "inventory" && r.station==="Standard" && r.width<=2 && r.height<=2 ||
      value === "table" && r.station==="Standard" || value === "bench" && r.station==="Metalworking" || value === "mortar" && r.station==="Mortar") &&
      (!q || matches(items.get(r.output),q) || r.cells.some(c=>c.id && matches(items.get(c.id),q))));
    const shownSmelt = (value==="all" || value==="smelting") ? data.smelting.filter(r=> matches(items.get(r.input),q) || matches(items.get(r.output),q)) : [];
    document.querySelector("#recipe-count").textContent = `${shown.length} crafting ${shown.length === 1 ? "pattern" : "patterns"} · ${shownSmelt.length} smelting ${shownSmelt.length === 1 ? "recipe" : "recipes"}`;
    result.innerHTML = shown.length ? `<h2>Crafting patterns</h2><div class="recipe-grid">${shown.map(recipeCard).join("")}</div>` : "";
    smelt.innerHTML = shownSmelt.length ? `<h2>Smelting and cooking</h2>${smeltTable(shownSmelt)}
      <h3 class="section">Fuels</h3><p>${data.fuels.map(f=>`${itemLink(f.id)} (${number(f.seconds)}s in a furnace)`).join(", ")}. Faster machines also use fuel faster.</p>` : "";
    document.querySelector("#no-recipes").hidden = shown.length + shownSmelt.length > 0;
  }
  search.addEventListener("input",render);
  filter.addEventListener("input",render);
  render();
  const target = document.getElementById(location.hash.slice(1));
  if (target) target.scrollIntoView();
}
function entry() {
  const requestedItem = params.has("item") ? items.get(Number(params.get("item"))) : null;
  const block = params.has("block") ? blocks.get(Number(params.get("block"))) : blocks.get(requestedItem?.block);
  const item = block ? items.get(block.id) : requestedItem;
  const info = block || item;
  if (!info) {
    main.innerHTML = '<h1>Entry not found</h1><p><a href="catalog.html">Return to the catalog</a>.</p>';
    return;
  }
  document.title = `${info.name} · MDSM Wiki`;
  const facts = [[block ? "Block ID" : "Item ID",info.id]];
  if (block) {
    facts.push(["Shape",block.shape],["Collision",block.solid ? "Solid" : "Non-solid"],["Opaque",block.opaque ? "Yes" : "No"],["Inventory form",block.placeable ? "Yes" : "No"],["Affected by gravity",block.falls ? "Yes" : "No"]);
    if (block.breakable) facts.push(["Preferred tool",block.tool || "Any"],...(block.tier !== "None" ? [["Harvest tier",block.tier+" or higher"]] : []));
    else if (block.key === "Bedrock") facts.push(["Survival mining","Unbreakable"]);
    if (block.stage) facts.push(["Growth stage",`${block.stage} / 7`]);
  }
  if (item) {
    facts.push(["Max stack",item.stackLimit]);
    if (item.durability) facts.push(["Durability",number(item.durability)]);
    if (item.tool !== "None") facts.push(["Tool",item.tool],...(item.tier!=="None"?[["Tier",item.tier]]:[]));
    if (item.armor !== "None") facts.push(["Armor slot",item.armor],["Armor points",item.armorPoints]);
    if (item.attackDamage>1) facts.push(["Attack damage",item.attackDamage]);
    if (item.lavaResistant) facts.push(["Dropped item survives lava","Yes"]);
  }
  const crafted = item ? data.recipes.filter(r=>r.output===item.id) : [];
  const used = item ? data.recipes.filter(r=>r.cells.some(c=>c.id===item.id)) : [];
  const smelting = data.smelting.filter(r=> item && (r.input===item.id || r.output===item.id) || block && r.machines.some(m=>m.block===block.id));
  const mobDrops = item ? data.mobs.filter(m=>m.drop===item.id) : [];
  main.innerHTML = `<p class="breadcrumb"><a href="catalog.html?kind=${block ? "blocks" : "items"}">${block ? "Blocks" : "Items"}</a> / ${html(info.name)}</p>
  <div class="entry-heading">${sprite(info,block ? "block" : "item")}<h1>${html(info.name)}</h1></div>
  <div class="columns"><div class="box"><h2>Overview</h2><p>${html(block ? describeBlock(block) : describeItem(item))}</p>
  ${mobDrops.length ? `<p>Mob drop listed for ${mobDrops.map(m=>`<a href="guide.html#mobs">${html(m.name)}</a>`).join(", ")}.</p>`:""}</div>
  <aside class="box"><h2>Properties</h2><dl class="facts">${facts.map(([key,value])=>`<dt>${html(key)}</dt><dd>${html(value)}</dd>`).join("")}</dl></aside></div>
  ${crafted.length ? `<section class="section"><h2>Crafting recipes</h2><div class="recipe-grid">${crafted.map(recipeCard).join("")}</div></section>` : ""}
  ${smelting.length ? `<section class="section"><h2>Smelting and cooking</h2>${smeltTable(smelting)}</section>` : ""}
  ${used.length ? `<section class="section"><h2>Used in crafting</h2><ul class="links">${used.map(r=>`<li><a href="recipes.html#craft-${r.id}">${html(items.get(r.output).name)} × ${r.count}</a> <span class="metadata">· ${html(station(r))}</span></li>`).join("")}</ul></section>` : ""}`;
}
function normalized(value) { return value.trim().toLowerCase().replaceAll("_"," "); }
function matches(info,q) { return normalized(info.name).includes(q) || String(info.id)===q || !!info.key && info.key.toLowerCase()===q; }

const navigationSearch = document.querySelector("#site-search");
const quickResults = document.querySelector("#quick-results");
const referencePages = [
  {name:"Crafting and smelting",url:"recipes.html", words:"recipes kiln furnace smoker crafting"},
  {name:"Terrain generation",url:"world.html",words:"biomes caves ores clay dungeons far lands seas rivers"},
  {name:"Gameplay guide",url:"guide.html",words:"controls commands farming lighting mobs survival seasons"}
];
navigationSearch.addEventListener("input",()=>{
  const q=normalized(navigationSearch.value);
  if (!q) {quickResults.hidden=true; return;}
  const found=data.blocks.filter(b=>matches(b,q)).concat(placeable.filter(i=>matches(i,q)))
    .sort((a,b)=>Number(normalized(b.name).startsWith(q))-Number(normalized(a.name).startsWith(q)))
    .slice(0,7);
  const pageMatches=referencePages.filter(p=>`${p.name.toLowerCase()} ${p.words}`.includes(q));
  quickResults.innerHTML=pageMatches.map(p=>`<a href="${p.url}">${p.name}</a>`).join("") + found.map(e=>`<a href="${"shape" in e ? blockUrl(e.id):itemUrl(e)}">${html(e.name)} <span class="metadata">#${e.id}</span></a>`).join("")
    + `<a href="catalog.html?q=${encodeURIComponent(navigationSearch.value)}">Search the catalog</a>`;
  quickResults.hidden=false;
});
document.addEventListener("keydown",event=>{if(event.key==="Escape"&&!quickResults.hidden) {quickResults.hidden=true; navigationSearch.focus();}});
document.addEventListener("pointerdown",event=>{if(!event.target.closest(".global-search")) quickResults.hidden=true;});
if (document.body.dataset.page==="home") {
  document.querySelector("#craft-count").textContent = number(data.recipes.length);
  document.querySelector("#block-count").textContent = number(data.blocks.length);
  document.querySelector("#item-count").textContent = number(placeable.length);
} else if (document.body.dataset.page==="catalog") catalog();
else if (document.body.dataset.page==="recipes") recipes();
else if (document.body.dataset.page==="entry") entry();
else if (document.body.dataset.page==="guide") {
  document.querySelector("#mob-rows").innerHTML=data.mobs.map(m=>`<tr><td>${html(m.name)}</td><td>${m.id}</td><td>${m.hostile?"Hostile":"Passive"}</td><td>${m.health}</td><td>${itemLink(m.drop)} (up to ${m.maximumDrop})</td></tr>`).join("");
}
})();
