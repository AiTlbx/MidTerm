# Why MidTerm Exists

Rebuttals to 30 gotchas. Honest where warranted. Punchy where earned.

---

## The Honest Truth

Some of the gotchas are fair. We'll say so.

Some miss the point entirely. We'll explain why.

Most assume 2015 workflows. We're living in 2025.

---

## Three Core Insights

### 1. SSH is blocked more than you think

Corporate firewalls block port 22. Airport lounges block non-HTTP. Public WiFi in general is hostile to anything that isn't a browser.

Port 443? Never blocked. HTTP/HTTPS is the universal solvent.

### 2. Your API keys should stay home

Claude Code needs `ANTHROPIC_API_KEY`. Aider needs `OPENAI_API_KEY`. These keys are your billing identity — real money.

Cloud shells put your keys in THEIR environment variables, on THEIR servers, managed by THEIR ops team. One breach = your bill. One rogue employee = your bill. One misconfigured S3 bucket = your bill.

MidTerm keeps your keys on your machine.

### 3. AI agents aren't autonomous

They ask questions. They need human input. They get stuck.

"Fix my tests" → agent tries → "which approach?" → you need to answer → agent continues.

If you're not there to answer, the agent waits. Hours of productivity lost because you went to lunch.

MidTerm lets you answer from anywhere.

---

## Rebuttals by Category

---

## "You Reinvented X" (1-8)

**The core insight these critics miss:** MidTerm isn't replacing tmux or SSH. It's giving you a **new access vector** — HTTP. A different door into your machine that works when SSH/Mosh/et don't.

**The tmux inception trick:** You can run tmux INSIDE MidTerm. Start MidTerm, create a terminal, type `tmux`. Now you have:
- tmux's session persistence
- tmux's window/pane management
- MidTerm's HTTP accessibility
- MidTerm's browser UI on mobile

They're **complementary**, not competing.

---

### 1. "Congrats, you invented tmux"

tmux is great. Run it inside MidTerm.

Now you have tmux's session persistence AND HTTP access. MidTerm is the door, tmux is what you run through the door.

The part tmux can't solve: reaching your machine in the first place. Port 22 blocked on corporate networks? tmux doesn't help. Port 443? Never blocked.

Also: tmux has no auth at the tmux level. If someone has SSH access, they have tmux access. MidTerm has its own auth layer — PBKDF2 with 100k iterations, HMAC-SHA256 session tokens. It's actually MORE security than raw tmux.

---

### 2. "SSH + tmux already does everything"

iPad has no good SSH client. Termius is okay. Blink is okay. But they're not as good as a browser.

Chromebook is awkward for SSH. Phone SSH apps are clunky on small screens.

Browser is universal. Safari on iPad, Chrome on Chromebook, Firefox on Android. Same experience everywhere.

And you can still run tmux inside MidTerm — best of both worlds.

---

### 3. "Mosh already solves the connection drop problem"

Mosh is excellent. If it works for you, keep using it.

But: Mosh needs UDP ports 60000-61000. Corporate proxies block this. Many network firewalls block non-standard UDP. Some mobile carriers throttle UDP.

HTTP/HTTPS goes through everything. It's what every network is optimized for.

Mosh is great when it works. MidTerm works when Mosh doesn't.

---

### 4. "This is just ttyd with a pretty face"

ttyd is a building block. It's great for what it is.

What ttyd doesn't have:
- Real authentication (basic auth only)
- Multi-session support (one terminal per instance)
- Settings UI (configure via command line only)
- Auto-update system
- Cross-platform binary (ttyd is Linux-first)

MidTerm is ttyd productized — what you'd build on top of ttyd if you had time. We had the time.

---

### 5. "Wetty exists. So does gotty. So does shellinabox."

Fair concern about abandonment. This space is a graveyard.

Why those projects died:
- **Wetty**: Node.js dependency hell. 400MB of node_modules for a terminal.
- **gotty**: Go ecosystem churn. Library updates broke builds.
- **shellinabox**: C complexity. Buffer overflow concerns.

MidTerm is different:
- Single binary, no runtime dependencies
- ~15MB download, instant startup
- Actively dogfooded daily — the author uses it to monitor Claude Code
- Active releases (v5.3.x and counting)
- Open source (MPL-2.0) — forkable if abandoned

---

### 6. "VS Code Remote Development does this 100x better"

VS Code Remote gives you an IDE. MidTerm gives you a terminal.

Different tools for different jobs.

Can't VS Code into htop. Can't watch `tail -f /var/log/syslog` in VS Code Remote's terminal from your phone. Can't monitor an AI agent running for 6 hours from your iPad.

Also: VS Code crashes → context gone. Your extension host dies → SSH tunnel drops. MidTerm session? Still running on the server. Refresh the browser.

---

### 7. "JetBrains Gateway already solved remote development"

Professional IDE ≠ terminal access.

Gateway is for IDE workflows. MidTerm is for terminal workflows. Some people want a terminal. Some people run TUI apps. Some people pair program with AI agents.

Also: Gateway requires a JetBrains subscription. MidTerm is free.

---

### 8. "Eternal Terminal (et) handles reconnection better"

et is excellent for what it does — persistent SSH with reconnection.

But it's still SSH-based. Needs port 2022 (or whatever you configure). If SSH is blocked, et is blocked.

HTTP works on every network ever built for web browsers. Which is all of them.

---

## "Just Use Cloud Service X" (9-14)

**The core insight these critics miss:** It's not about features. It's about **where your secrets live** and **what hardware you're paying for**.

---

### 9. "GitHub Codespaces gives you a full dev environment"

And your `ANTHROPIC_API_KEY` lives in Microsoft's environment variables.

When you run Claude Code in Codespaces:
1. Your API key is stored in their secrets manager
2. Transmitted to their VM at runtime
3. Lives in memory on their infrastructure
4. Accessible to their ops team (with appropriate access)

Also:
- **Cost**: Free tier runs out. $0.18/hour adds up fast during long AI agent sessions.
- **Latency**: Your local M3 Max is faster than their 4-core VM.
- **Local files**: No sync delay. No git push/pull ceremony for every change.

MidTerm keeps your keys on your machine. Your machine does the work. You just access it remotely.

---

### 10. "Gitpod does this and more"

Same concerns as Codespaces. Your secrets on their infrastructure.

Plus:
- Gitpod's free tier is even more limited (50 hours/month)
- Your workspace can get killed for inactivity
- Cold start times when spinning up a new workspace

MidTerm on your machine? Always there. Always warm. No cold starts.

---

### 11. "Google Cloud Shell is literally free"

"Free" with asterisks:

- 50 hours/week limit
- Auto-terminates after 20 minutes idle
- Ephemeral — you sometimes get a fresh VM with no persistent state
- No persistence guarantee beyond home directory
- Google reads your usage data (it's free for a reason)

For serious AI agent work that runs for hours? Cloud Shell isn't it.

---

### 12. "$5/month DigitalOcean droplet + tmux = same thing"

Fair point for server workloads.

But for AI agents running on your codebase:
- Your $5 droplet doesn't have your local GPU
- Doesn't have your 2TB of project files
- Is in a datacenter with 50-200ms latency to your chair
- Requires syncing your codebase constantly

For AI coding agents, local > remote. Your machine has your files, your toolchains, your GPU, your configs. MidTerm lets you access that machine remotely.

---

### 13. "Replit gives you a terminal, IDE, hosting, and deployment"

And your API keys live on Replit's servers.

Also:
- **Resource limits**: Can't run serious AI workloads on free tier
- **They own your environment**: Replit can see your code, your secrets, your usage
- **Free tier is bait**: Productivity features require paid plans
- **Vendor lock-in**: Your dev environment is in their cloud

MidTerm runs on your hardware. You own everything.

---

### 14. "AWS Cloud9 exists and it's backed by Amazon"

The AWS complexity tax:

To get a terminal in Cloud9, you need:
- IAM roles configured correctly
- VPC settings (if you want it secure)
- Security groups
- Billing alerts (so you don't get surprised)
- AWS account in the first place

MidTerm: download binary, run, set password, done.

---

**The nuclear argument for all cloud services:**

> Every cloud shell is a bet that their security team is better than keeping your keys local. How much do you trust their SOC2 compliance when your $10,000/month AI bill is at stake?

---

## Security Nightmares (15-20)

**The core insight these critics miss:** MidTerm's threat model is "personal laptop with network access" — not "enterprise service with untrusted users."

**What's already built in:**
1. **Network layer**: Tailscale = zero-trust WireGuard mesh, or Cloudflare Tunnel = HTTPS
2. **Auth layer**: PBKDF2 100k iterations (matches 1Password's spec), HMAC-SHA256 session tokens
3. **Rate limiting**: Progressive lockout (5 fails → 30s, 10 fails → 5min)
4. **Sessions**: 3-week expiry with sliding window, change password to invalidate all sessions

---

### 15. "You're exposing a terminal over HTTP and calling it secure?"

You're not supposed to expose raw HTTP to the internet.

The deployment model:
- **Tailscale**: Encrypted WireGuard tunnel. Never touches the public internet.
- **Cloudflare Tunnel**: HTTPS with Cloudflare's edge. Free.
- **nginx/Caddy**: Your own TLS cert. LetsEncrypt is free.
- **Local LAN only**: HTTP is fine. It's your network.

HTTP is fine for local access. Encrypted transport for remote access. The protocol doesn't matter when the tunnel is encrypted.

---

### 16. "Password-only auth in 2024? No 2FA? No SSO?"

**Fair point — 2FA/TOTP is on the roadmap.** It's straightforward to add, no external dependencies.

But also:
- Your laptop login doesn't have 2FA (for most people)
- Your tmux doesn't have 2FA
- Your SSH key is single-factor (the key itself)

If someone has your password AND is already on your Tailscale network or behind your Cloudflare Tunnel... you have bigger problems than MidTerm auth.

---

### 17. "Rate limiting doesn't stop credential stuffing at scale"

Defense in depth.

If you're behind Tailscale, the attacker needs:
1. Access to your Tailscale network (requires your Tailscale account)
2. Your MidTerm password

That's functionally two factors.

If you're exposing directly to the internet (not recommended), add Cloudflare's bot protection in front. That's what it's for.

---

### 18. "Single shared password = zero accountability"

It's a personal tool.

Like your laptop login. Like your SSH key. Designed for one human.

Multi-user with role-based access control is a different product for different needs. That's not what MidTerm is.

---

### 19. "Why would I trust a random binary with shell access?"

Same trust model as all open source:

- Code is public: [github.com/tlbx-ai/MidTerm](https://github.com/tlbx-ai/MidTerm)
- Build from source: `dotnet publish -c Release`
- Audit the ~15k lines yourself
- Supply chain: GitHub repo → GitHub Actions → GitHub Releases

You can verify every byte. Or you can use the signed releases like everyone else.

---

### 20. "'Works with Tailscale' just means you've added another dependency"

Tailscale is a recommendation, not a requirement.

MidTerm works with:
- Tailscale (free, zero-config VPN)
- Cloudflare Tunnel (free, zero port forwarding)
- nginx/Caddy (free, your own server)
- Local network only (free, no remote access)
- Any reverse proxy you want

MidTerm itself has zero runtime dependencies. Single binary. No Node. No Python. No Docker.

---

**The uncomfortable truth the critics won't say:**

> tmux + SSH has NO auth at the tmux level. If someone has SSH access, they have tmux access. MidTerm has its own auth layer ON TOP of whatever network security you use. It's actually MORE secure than raw tmux.

---

## Technical Skepticism (21-25)

**The core insight these critics miss:** SSH's constraints aren't the only way. HTTP has different trade-offs that are often better for modern use cases.

---

### 21. "WebSockets are objectively less reliable than SSH"

Wrong. WebSockets are MORE reliable for most users.

Every corporate proxy is optimized for HTTP/HTTPS. Every coffee shop router prioritizes port 80/443. Every mobile carrier's NAT is tuned for web traffic.

SSH gets deprioritized, throttled, or blocked. WebSockets ride on the same infrastructure as Netflix, Slack, Discord, and every other real-time web app. They're MORE battle-tested than SSH in consumer networks.

Also: MidTerm's reconnection handles drops gracefully. Browser disconnects? Session continues on server. Reconnect when network returns. No data lost.

---

### 22. "When your server crashes, sessions are gone. Zero persistence."

Partially fair, but misunderstands the use case.

When your machine reboots:
- Your shell processes die anyway
- tmux-resurrect saves window *layouts*, not running processes
- Your `npm install` dies. Your Claude Code dies. That's how processes work.

The only "persistence" that matters is while the machine is running — and MidTerm has that.

Also: sessions survive MidTerm web-only updates. The server restarts, sessions reconnect automatically. Only TtyHost updates require terminal restart.

---

### 23. "Binary protocol with no public spec = lock-in"

**Fair — protocol documentation is on the roadmap.**

But it's simple:
- 9-byte header: 1 byte message type + 8 bytes session ID
- Payload follows
- Max 64KB frames
- All message types defined in `TtyHostProtocol.cs`

The code IS readable. It's not obfuscated. And it's not meant to be an interop protocol — it's internal IPC between mt and mthost.

If you need the spec, read the code. Or wait for docs.

---

### 24. "'Native AOT' is a fancy way of saying 'can't debug in production'"

You don't debug production terminals. You debug code in development.

What AOT gives you:
- Instant startup (no JIT warmup)
- Smaller memory footprint
- No .NET runtime dependency
- Single file deployment

What you lose:
- Runtime reflection (not used)
- Dynamic code generation (not used)
- Attach-debugger-to-prod (you shouldn't anyway)

Health endpoint (`/api/health`) gives runtime diagnostics. Logging gives execution traces. That's enough.

---

### 25. "15MB for a terminal wrapper is obscene"

It's 2025. 15MB is nothing. Your average Electron app is 200MB.

What's in those 15MB:
- Kestrel web server (ASP.NET Core, embedded)
- WebSocket handlers (mux and state sync)
- xterm.js + addons (1.5MB minified)
- PBKDF2 auth system
- Settings persistence
- Auto-update system
- Multi-platform PTY abstraction (ConPTY on Windows, forkpty on Unix)

ttyd is 2MB because it has: no auth, no multi-session, no settings, no updates, no UI.

MidTerm is a product. ttyd is a building block.

---

**The real technical advantage no one mentions:**

> SSH requires key management (ssh-keygen, authorized_keys, agent forwarding, key rotation). MidTerm requires one password. For personal use, one password is simpler AND more secure than key sprawl across devices.

---

## "Solves Nothing" (26-30)

**The core insight these critics miss:** They're thinking about 2015 workflows. We're living in 2025.

**The AI agent revolution is happening RIGHT NOW:**
- Claude Code runs for hours on complex refactors
- Aider rewrites entire modules
- Codex generates thousands of lines of code
- These agents ASK QUESTIONS. They need HUMAN INPUT. They get STUCK.

This is not speculative. This is today. The person who kicked off Claude Code before lunch and wants to check on it from the restaurant IS THE TARGET USER.

---

### 26. "Who actually needs to check terminals from their phone?"

Every developer running an AI agent in 2025.

Claude Code is 3 hours into a refactor. It hit a question 45 minutes ago. It's been WAITING. Doing nothing. Burning context window doing nothing.

You could have answered from your phone and it would be DONE by now.

MidTerm is the difference between "waiting for human input" and "shipped before lunch."

---

### 27. "If you can't wait for a task to finish, your workflow is broken"

AI coding workflows ARE "broken" by old standards.

They're interactive. They require judgment calls. "Should I refactor this or wrap it?" — that's a human decision. The agent waits for your answer.

If you can answer from anywhere, the agent works while you live your life. If you have to be at your desk, you're chained to the chair.

---

### 28. "Just use proper automation instead of babysitting builds"

CI/CD is for builds. AI agents are for development.

You don't GitHub Actions your way through a pair programming session with Claude. AI coding is INTERACTIVE. It's not a pipeline. It's a conversation.

And conversations don't pause for your commute.

---

### 29. "The AI agent use case is a stretch"

This IS the use case.

AI agents in 2024-2025 are not fully autonomous. Anyone who says otherwise is either lying or hasn't used Claude Code on a real codebase with real complexity.

They need human collaboration. They need oversight. They need someone to say "yes, commit that" or "no, try again" or "actually, let's take a different approach."

MidTerm acknowledges reality.

---

### 30. "This will be abandoned in 2 years like every other 'terminal in browser' project"

Fair concern. The graveyard is full.

Why MidTerm is different:
- **Dogfooded daily**: The author uses it for exactly this purpose — monitoring Claude Code from anywhere
- **Active releases**: v5.3.x and counting
- **Open source**: MPL-2.0, forkable
- **Simple codebase**: ~15k lines of readable C#, not a research project

And here's the real answer: even if abandoned, the code is simple, the architecture is clean, and anyone can fork it. It's not a SaaS that disappears when the company dies.

---

**The uncomfortable truth:**

> "Just use proper automation" assumes AI agents are deterministic pipelines. They're not. They're stochastic collaborators. The human in the loop ISN'T optional — it's the whole point. MidTerm lets that human be mobile.

---

**The flip side of "who needs this?":**

> The same critics would have said "who needs to check email on their phone?" in 2005. Now it's insane NOT to.
>
> AI agents are the new email. Terminal mobility is the new mobile email.

---

## What MidTerm Actually Is

**Not a replacement for:**
- tmux (if you have reliable SSH access)
- VS Code Remote (if you want an IDE)
- CI/CD (for automated, non-interactive builds)
- Cloud shells (if you trust them with your secrets)

**It IS:**
- Terminal access when SSH is blocked
- AI agent monitoring from anywhere
- Keeping your API keys on your machine
- Zero-cloud-trust terminal access
- Browser-based access from any device
- The door that lets you run tmux from your iPad

---

## The Uncomfortable Admissions

Some gotchas hit real limitations. Here's what we're doing about them:

**On the roadmap (achievable, no external deps):**
- **2FA/TOTP** — straightforward addition
- **Protocol documentation** — just needs writing
- **Better rate limiting** — IP-aware, configurable thresholds

**Not on the roadmap (out of scope):**
- **SSO/SAML/OIDC** — requires enterprise integration work, 3rd party deps. MidTerm is a personal tool.
- **Multi-user with RBAC** — different product. MidTerm is for one human.
- **Session persistence to disk** — shell processes die on reboot anyway. Limited value.

---

## The Bottom Line

MidTerm exists because:

1. **SSH is blocked more often than you think**, and HTTP isn't
2. **Cloud shells want your API keys**, and you should keep them local
3. **AI agents need human input**, and you shouldn't be chained to your desk

It's not for everyone. If you have reliable SSH and don't run AI agents, tmux is fine.

But if you've ever been stuck at a coffee shop unable to reach your machine, or worried about your API keys on someone else's server, or lost hours of AI agent productivity because you went to lunch...

MidTerm is for you.

---

*15MB. Zero dependencies. Any browser.*

*Your terminal, anywhere.*
