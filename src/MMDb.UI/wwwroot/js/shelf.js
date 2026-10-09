// Ctrl+K (Cmd+K on a Mac keyboard) jumps to the topbar search, as its hint says.
document.addEventListener("keydown", e => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
    const search = document.querySelector("[data-search]");
    if (search) {
      e.preventDefault();
      search.focus();
      search.select();
    }
  }
});
