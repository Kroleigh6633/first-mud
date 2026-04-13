# Quest Chains — First Three

Three named chains, each tied to a faction's agenda. Each has 3–5 beats, named NPCs, stakes, success/failure states, and specific reputation deltas.

Convention: reputation changes are in tier-points as per `lore/reputation.md` (1–999 Known, 1000–2999 Trusted, etc.). Where the doc says "+200 Thornwood," that is raw points within the current tier.

---

## Chain 1 — **The Sealed Letter** (Gravenguard, single-zone → multi-zone)

**Agenda:** Commander Solan Drest needs to know what his missing expedition sent back before they vanished — without his lieutenants seeing the answer.

**Quest-giver:** *Kesh of Reed-End* — Gravenguard scout, mid-20s, speaks in half-sentences, finishes them only if you wait. Brother to Orla, who went with the Lost Expedition.

### Beat 1 — "A Letter for the Commander's Eyes"
Kesh intercepts the player on the Gravenmarsh Causeway. Hands over a sealed letter and a silver signet. Instruction: deliver directly to Commander Drest, no intermediary, and *accept no escort*, especially not Lieutenant Varn.
- **Stakes:** Kesh will be reassigned or worse if the letter is read by anyone else.
- **Reward:** +150 Gravenguard (Known).
- **Branching choice:** On arrival at Gravenhold, Lieutenant Varn intercepts the player and demands the letter "for the Commander's schedule." Hand it over → Beat 2a. Refuse → Beat 2b.

### Beat 2a — "The Convenient Loss" (branch: gave letter to Varn)
Varn thanks the player and dismisses them. A day later, Kesh is found face-down in the bog; "exposure." Drest sends for the player, calm, and asks what was in the letter the player never delivered. Player can confess or lie.
- **Confess:** Drest respects it. +100 Gravenguard. Varn's treachery becomes a later beat.
- **Lie (and get caught):** -400 Gravenguard, permanent floor at Trusted.

### Beat 2b — "Past the Lieutenant" (branch: refused Varn)
Player must reach Drest's private chambers without Varn's permission. Options: bribe the night-scribe (gold cost), use the servants' stair (Thornwood rep helps — a sympathetic scullery mage), or invoke the Rider's privilege of direct monarch-service (only works if the player has not used it recently; once-per-arc resource).
- **Success:** +250 Gravenguard. Drest reads the letter in front of the player. He goes very still.

### Beat 3 — "What Orla Saw"
Drest confides that the letter contained a sketch — an Ardweld glyph the Lost Expedition found *inscribed on the skeleton of something that should not have had bones*. He asks the player to ride to Eastmile and fetch Brother Velm, who once catalogued glyphs for the Thornwood and will recognize this one.
- **Stakes:** If Varn is still operational, he will send someone to intercept on the road.
- **Reward:** +300 Gravenguard. Unlocks Brother Velm as periodic advisor NPC.

### Beat 4 — "Translation"
Brother Velm, brought to Gravenhold, reads the glyph and breaks down. It is a *naming* — Ardweld funerary magic that names a thing so it cannot come back. Something the Expedition encountered was already dead, and someone had already un-killed it.
- **Success state:** Player learns: the Lost Expedition found evidence of Aether/Unmaking at Ardweld-ruin scale. Drest promotes the player's access. +500 Gravenguard (likely pushes to Trusted). Unlocks Ardweld Remnant discovery quest precursor.
- **Failure state:** If Velm was not retrieved alive — ambush on the road, player routed — Drest seals the matter. No reputation loss, but the Remnant chain is gated behind a later, harder alternate entry.
- **Soft-failure:** Player delivers Velm but Velm has already been Dream-Walked by an Ashen agent (triggers if player dallied more than a set number of in-game days between Beat 3 and Beat 4). Velm lies about the glyph. Player may or may not catch it. Consequence seeds a much later mid-chain problem.

**Reputation summary:** Up to +1050 Gravenguard across the chain. -50 to -400 Gravenguard on bad branches. No other factions affected unless the player invoked Thornwood help in Beat 2b (+50 Thornwood for the scullery favor).

---

## Chain 2 — **The Widow's Coven** (Thornwood, multi-zone, branching)

**Agenda:** Eldest Mira Ashvale needs to know whether a dead witch's working is still active — and whether her daughter carried it on.

**Quest-giver:** *Auld Maerwyn of the Holt* — hedge-witch, seventy-something, speaks in cooking metaphors, is sharper than she looks.

### Beat 1 — "The Garden Has Gone Quiet"
Maerwyn asks the player to visit Widow Moss in Eastmile. Maerwyn has not had a bird from her in ten days. The player knocks; the door is unlocked; the cottage is *deliberately* empty — not fled, packed.
- **Clue:** A kettle is still warm. A coven-mark is chalked backwards above the hearth — a warning, not a signature.
- **Reward:** +100 Thornwood for bringing the news back.

### Beat 2 — "Who Took Her"
Maerwyn sends the player to track Moss. Two trails exist: toward Caervorn Marchkeep (arrest?) and toward the Thornwood Verge (flight?). The player picks one to follow.

- **Branch A (Marchkeep):** Moss is in the Assay Office cells, awaiting transfer to the Ironspire. Ewan Truce has a ledger entry: "witch, confessed." She has not confessed. Rescue options: bribe, break out, or formal Rider-writ (burns a commission the player is holding). Each has cost.
- **Branch B (Verge):** Moss is hiding at the Gorsewitch Circle, frightened. Someone in the village sold her. She names the buyer: a Compact factor visiting monthly named Halven Orys.

Both branches converge at Beat 3 but carry different information forward.

### Beat 3 — "The Daughter"
Maerwyn reveals: Moss has a daughter, Rhianne, an apprentice at the Rootweave, who is currently coven-Working something the Eldest did not authorize. Moss's arrest or flight was *cover* — the daughter is the actual operation. The player must travel deep into the Thornwood to the Rootweave, find Rhianne, and either stop her or help her complete the Working.

- **Stakes:** The Working is a long-range *warding* of Eastmile against Caervorn incursion. Eldest Mira forbade it because the Rootweave cannot spare the power. Rhianne is doing it anyway. Success warps her — Weave-overdraw at apprentice level.
- **Branching choice:**
  - **Stop her:** +300 Thornwood (Eldest approves). Rhianne is furious. Eastmile remains unwarded.
  - **Help her:** -200 Thornwood (Eldest's authority undermined). Rhianne survives because the player stabilized her Weave. Eastmile gains a permanent ward — when Caervorn garrisons Eastmile in the evolved-state zone pass, they find the village harder to hold. Long-tail consequence on Chain 3.
  - **Let her finish unaided:** Rhianne dies. -400 Thornwood. Moss disappears entirely. The ward takes — but paid for in a life the coven did not sanction.

### Beat 4 — "The Factor's Ledger" (only if Branch B was taken in Beat 2)
Halven Orys, the Compact factor, is the informant. He sold Moss's location to Caervorn for coin. The player can confront, bribe, report, or ignore.
- **Report to Compact:** +100 Compact (factors policing factors is good for business). Orys is quietly removed.
- **Report to Thornwood:** +200 Thornwood. Orys is quietly *un-remade* on a road some months later; the player is not told.
- **Blackmail Orys:** One-time gold payout, -0 reputation, but Orys shows up in later arcs as an enemy.

**Reputation summary:** +100 to +600 Thornwood across the chain; -200 to -400 Thornwood on the wrong branch; -100 Caervorn if Branch A's rescue was messy; +100 Compact possible in Beat 4.

**Success state:** Eldest Mira invites the player to a Coven-Working session (unlocks Coven-Working research access). **Failure state:** Maerwyn stops opening her door.

---

## Chain 3 — **The Ironspire Summons** (Caervorn, spans Eastmile → Marchkeep → Ironspire)

**Agenda:** Lord-Marshal Aldric Caervorn wants to audit a Rider to test a theory: that Riders are a vector for Thornwood intelligence, and that the King's Road system is a Coven infiltration network.

**Quest-giver:** *Captain Bryn Hollemar*, Marchkeep — delivers the summons reluctantly; he believes it's a setup.

### Beat 1 — "The Summons"
Hollemar passes a sealed Caervorn writ at the Bent Mare. The player is ordered to present themselves at the Ironspire within twelve in-game days for a formal inquiry. Declining is possible — costs -500 Caervorn and a permanent mark on the Rider's Post record.
- **Stakes:** Accepting means walking into Aldric's house as a suspected spy. Hollemar slips the player a second, unsealed note: *"Bring nothing the Assayer cannot explain."* Meaning: ditch the brooch, or explain it.
- **Reward (for accepting):** +50 Caervorn (compliance noted). Nothing else yet.

### Beat 2 — "The Assay"
At Marchkeep, Ewan Truce formally assays the player's effects. The brooch is the problem. Options:
- **Hide it (Sleight of Hand / Air Illusion check):** Success → skip to Beat 3 clean. Failure → seizure + -300 Caervorn, but the player still proceeds to Ironspire *without* the brooch, a significant magical handicap for the rest of the chain.
- **Declare it as inherited jewelry:** Truce records it and lets it through with a warning tag. The player keeps it but is flagged.
- **Bribe Truce:** Only works if the player knows his secret (that he kept an artifact). Requires a Gravenguard Trusted-tier clue from Chain 1's periphery. Clean pass. +0 reputation.

### Beat 3 — "The Inquiry"
At the Ironspire, Aldric himself questions the player across three sittings: road knowledge, faction contacts, wyrd. He is not shouting. He is *testing*. Every answer is weighed against the player's known rep: if the player is Thornwood Trusted or higher, Aldric already knows — the question is whether the player will lie about it.

- **Lie and get caught:** -800 Caervorn (Hostile floor), expelled, hunted in Caervorn territory for the rest of the current arc.
- **Tell truth strategically:** +300 Caervorn. Aldric is disappointed but not threatened. The player has "proven useful honesty."
- **Tell truth completely, including Thornwood ties:** -100 Caervorn, +200 Thornwood (word gets back — Caervorn nobility has Thornwood sympathizers in its staff). Aldric marks the player as a "known variable," which is better than "suspected variable."

### Beat 4 — "The Task"
Aldric, satisfied or not, assigns the player a task as a loyalty test: ride to the Thornwood Verge, identify a specific coven stone, and return its precise location. This is reconnaissance for a future strike.
- **Branching choice:**
  - **Comply truthfully:** +400 Caervorn. -300 Thornwood. Aldric gains targeting data; later in the timeline a coven circle burns. The player will see it burn and know why.
  - **Comply falsely (give wrong coordinates):** +200 Caervorn *if undetected* (requires Thornwood Trusted+ to know which false coords are plausible). -100 Thornwood (they notice you're meddling even if you're helping).
  - **Refuse:** -400 Caervorn, permanent Wary floor. +200 Thornwood. Aldric will not ask the player again — but he has a file on them now.
  - **Warn the Thornwood first, then comply with real coords:** +300 Caervorn, +0 Thornwood net (the coven relocates the stone; Aldric's strike hits an empty circle; he suspects the player but cannot prove it). High-risk, high-information outcome.

**Reputation summary:** Up to +750 Caervorn or down to -1200. Up to +200 Thornwood or down to -300. **Cross-faction lock:** The Thornwood ↔ Caervorn tension in canon means any Caervorn gains here automatically apply tier reductions to Thornwood per `lore/reputation.md`.

**Success state (Aldric's view):** Player is added to the Caervorn "useful Riders" register. Unlocks quest access to Caervorn arms and early information about the Ashen Court's silence (Aldric knows more than he says). **Failure state:** Permanent Caervorn Wary. Ironspire quest access closed until a mid-game reconciliation arc.

**Intersection with other chains:** If Chain 2's Beat 3 left Eastmile warded (Rhianne helped), Aldric in Beat 3 specifically asks about the ward — he knows it exists. Lying about that in front of him is a Wyrd-touched moment (the brooch pulses; Aether-sensitive NPCs present, if any, can feel it).
