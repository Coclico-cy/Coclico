'use strict';

(function initCursor() {
  const dot  = document.getElementById('cursor');
  const glow = document.getElementById('cursor-glow');
  if (!dot || !glow) return;
  let mx = 0, my = 0, dx = 0, dy = 0;

  document.addEventListener('mousemove', e => {
    mx = e.clientX; my = e.clientY;
    dot.style.transform  = `translate(${mx}px, ${my}px)`;
    glow.style.transform = `translate(${mx}px, ${my}px)`;
  });

  document.addEventListener('mouseenter', () => { dot.style.opacity = '1'; glow.style.opacity = '1'; });
  document.addEventListener('mouseleave', () => { dot.style.opacity = '0'; glow.style.opacity = '0'; });

  document.querySelectorAll('a, button, .module-card, .perf-card, .extra-item').forEach(el => {
    el.addEventListener('mouseenter', () => {
      dot.style.transform  += ' scale(2)';
      glow.style.opacity   = '0.5';
    });
    el.addEventListener('mouseleave', () => {
      glow.style.opacity = '1';
    });
  });
})();


(function initCanvas() {
  const canvas = document.getElementById('bg-canvas');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');

  let W, H, particles = [];

  const resize = () => {
    W = canvas.width  = window.innerWidth;
    H = canvas.height = window.innerHeight;
  };
  resize();
  window.addEventListener('resize', resize);

  const colors = ['rgba(74,108,247,', 'rgba(124,58,237,', 'rgba(6,182,212,'];
  const isReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const particleCount = isReducedMotion ? 35 : 55;
  const maxDistance = 110;

  for (let i = 0; i < particleCount; i++) {
    particles.push({
      x: Math.random() * W,
      y: Math.random() * H,
      vx: (Math.random() - 0.5) * 0.35,
      vy: (Math.random() - 0.5) * 0.35,
      r: Math.random() * 1.3 + 0.25,
      alpha: Math.random() * 0.35 + 0.1,
      color: colors[Math.floor(Math.random() * colors.length)]
    });
  }

  let lastFrame = 0;
  function draw(timestamp) {
    if (document.hidden) {
      setTimeout(() => requestAnimationFrame(draw), 1200);
      return;
    }

    if (timestamp - lastFrame < 28) {
      requestAnimationFrame(draw);
      return;
    }
    lastFrame = timestamp;

    ctx.clearRect(0, 0, W, H);

    for (const p of particles) {
      p.x += p.vx;
      p.y += p.vy;
      if (p.x < 0 || p.x > W) p.vx *= -1;
      if (p.y < 0 || p.y > H) p.vy *= -1;

      ctx.beginPath();
      ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
      ctx.fillStyle = p.color + p.alpha + ')';
      ctx.fill();
    }

    const active = particles.slice();
    for (let i = 0, len = active.length; i < len; i++) {
      const pi = active[i];
      for (let j = i + 1; j < len; j++) {
        const pj = active[j];
        const dx = pi.x - pj.x;
        const dy = pi.y - pj.y;
        const dist2 = dx * dx + dy * dy;
        if (dist2 < maxDistance * maxDistance) {
          const alpha = (1 - Math.sqrt(dist2) / maxDistance) * 0.075;
          ctx.beginPath();
          ctx.moveTo(pi.x, pi.y);
          ctx.lineTo(pj.x, pj.y);
          ctx.strokeStyle = `rgba(74,108,247,${alpha})`;
          ctx.lineWidth = 0.4;
          ctx.stroke();
        }
      }
    }

    requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);
})();


(function initNav() {
  const nav = document.getElementById('nav');
  if (!nav) return;
  const onScroll = () => nav.classList.toggle('scrolled', window.scrollY > 40);
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
})();


function closeMenu() {
  const burger = document.getElementById('navBurger');
  const mobile = document.getElementById('navMobile');
  if (burger) { burger.classList.remove('open'); burger.setAttribute('aria-expanded', 'false'); }
  if (mobile) mobile.classList.remove('open');
}

(function initMobileMenu() {
  const burger = document.getElementById('navBurger');
  const mobile = document.getElementById('navMobile');
  if (!burger || !mobile) return;
  burger.addEventListener('click', () => {
    const open = burger.classList.toggle('open');
    burger.setAttribute('aria-expanded', String(open));
    mobile.classList.toggle('open', open);
  });
})();


(function initReveal() {
  const els = document.querySelectorAll('[data-reveal]');
  if (!els.length) return;
  const obs = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (e.isIntersecting) {
        e.target.classList.add('revealed');
        obs.unobserve(e.target);
      }
    });
  }, { threshold: 0.12 });
  els.forEach(el => obs.observe(el));
})();


(function initPerfBars() {
  const bars = document.querySelectorAll('.perf-bar-fill');
  if (!bars.length) return;
  const obs = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (e.isIntersecting) {
        e.target.classList.add('animated');
        obs.unobserve(e.target);
      }
    });
  }, { threshold: 0.5 });
  bars.forEach(b => obs.observe(b));
})();


document.querySelectorAll('a[href^="#"]').forEach(a => {
  a.addEventListener('click', e => {
    const id = a.getAttribute('href').slice(1);
    if (!id) return;
    const target = document.getElementById(id);
    if (!target) return;
    e.preventDefault();
    const offset = 80;
    const top = target.getBoundingClientRect().top + window.scrollY - offset;
    window.scrollTo({ top, behavior: 'smooth' });
    closeMenu();
  });
});


(function initCounters() {
  const vals = document.querySelectorAll('.stat-val');
  if (!vals.length) return;
  const obs = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (!e.isIntersecting) return;
      const el = e.target;
      const raw = el.textContent.replace(/[^0-9]/g, '');
      const suffix = el.textContent.replace(/[0-9]/g, '');
      if (!raw) return;
      const target = parseInt(raw);
      let current = 0;
      const step = Math.ceil(target / 40);
      const timer = setInterval(() => {
        current = Math.min(current + step, target);
        el.textContent = current + suffix;
        if (current >= target) clearInterval(timer);
      }, 30);
      obs.unobserve(el);
    });
  }, { threshold: 0.8 });
  vals.forEach(v => obs.observe(v));
})();


document.querySelectorAll('.module-card').forEach(card => {
  card.addEventListener('mousemove', e => {
    const rect = card.getBoundingClientRect();
    const x = (e.clientX - rect.left) / rect.width  - 0.5;
    const y = (e.clientY - rect.top)  / rect.height - 0.5;
    card.style.transform = `translateY(-4px) rotateX(${-y * 5}deg) rotateY(${x * 5}deg)`;
  });
  card.addEventListener('mouseleave', () => {
    card.style.transform = '';
  });
});


(function initChatDemo() {
  const typingEl = document.querySelector('.chat-msg.typing');
  if (!typingEl) return;

  const aiResponse = 'Le RAM Cleaner peut libérer jusqu\'à 80% de ta mémoire vive en quelques secondes grâce à 18 opérations natives P/Invoke. Tu veux que je lance un nettoyage maintenant ?';

  const obs = new IntersectionObserver((entries) => {
    if (!entries[0].isIntersecting) return;
    obs.disconnect();

    setTimeout(() => {
      typingEl.classList.remove('typing');
      typingEl.innerHTML = '';

      let i = 0;
      const type = () => {
        if (i < aiResponse.length) {
          typingEl.textContent += aiResponse[i];
          i++;
          setTimeout(type, 18);
        }
      };
      type();
    }, 1800);
  }, { threshold: 0.5 });

  obs.observe(typingEl);
})();

