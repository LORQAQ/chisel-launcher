(function () {
  'use strict';

  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // 截图切换
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.tabs button'));
  var panes = tabs.map(function (t) { return document.getElementById(t.getAttribute('aria-controls')); });
  tabs.forEach(function (tab, i) {
    tab.addEventListener('click', function () { select(i); });
    tab.addEventListener('keydown', function (e) {
      var d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
      if (!d) return;
      e.preventDefault();
      var next = (i + d + tabs.length) % tabs.length;
      tabs[next].focus();
      select(next);
    });
  });
  function select(index) {
    tabs.forEach(function (t, i) { t.setAttribute('aria-selected', String(i === index)); });
    panes.forEach(function (p, i) { if (p) p.hidden = i !== index; });
  }

  // 滚动进场
  var targets = document.querySelectorAll('h2, .section-lede, .feat, .dl, .grid2, .build-h, details, .strip__in');
  if (!reduce && 'IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('on');
        io.unobserve(entry.target);
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    Array.prototype.forEach.call(targets, function (el) {
      el.classList.add('rv');
      io.observe(el);
    });
  }

  // 预取 star 数，失败就留空
  var slot = document.getElementById('stars');
  if (slot) {
    fetch('https://api.github.com/repos/LORQAQ/chisel-launcher', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) {
        if (!d || typeof d.stargazers_count !== 'number') return;
        slot.textContent = '★ ' + (d.stargazers_count >= 1000 ? (d.stargazers_count / 1000).toFixed(1) + 'k' : d.stargazers_count);
        slot.hidden = false;
      })
      .catch(function () {});
  }
})();
