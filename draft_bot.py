import json
import os
from dataclasses import dataclass, field
from typing import Dict, List, Optional

import discord
from discord.ext import commands

STATE_PATH = os.environ.get("DRAFT_STATE_PATH", "draft_state.json")
TOKEN = os.environ.get("DISCORD_TOKEN")
COMMAND_PREFIX = os.environ.get("DRAFT_COMMAND_PREFIX", "!")


@dataclass
class DraftState:
    captains: List[int] = field(default_factory=list)
    remaining_players: List[str] = field(default_factory=list)
    team_picks: Dict[str, List[str]] = field(default_factory=dict)
    turn_index: int = 0
    channel_id: Optional[int] = None
    message_id: Optional[int] = None

    @classmethod
    def load(cls, path: str) -> "DraftState":
        if not os.path.exists(path):
            return cls()
        with open(path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
        return cls(
            captains=data.get("captains", []) or [],
            remaining_players=data.get("remaining_players", []) or [],
            team_picks=data.get("team_picks", {}) or {},
            turn_index=int(data.get("turn_index", 0) or 0),
            channel_id=data.get("channel_id"),
            message_id=data.get("message_id"),
        )

    def save(self, path: str) -> None:
        data = {
            "captains": self.captains,
            "remaining_players": self.remaining_players,
            "team_picks": self.team_picks,
            "turn_index": self.turn_index,
            "channel_id": self.channel_id,
            "message_id": self.message_id,
        }
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, ensure_ascii=False, indent=2)

    def current_captain(self) -> Optional[int]:
        if not self.captains:
            return None
        return self.captains[self.turn_index % len(self.captains)]

    def register_pick(self, captain_id: int, player_name: str) -> None:
        if player_name not in self.remaining_players:
            raise ValueError("Player not available")
        self.remaining_players.remove(player_name)
        key = str(captain_id)
        self.team_picks.setdefault(key, []).append(player_name)
        if self.remaining_players:
            self.turn_index = (self.turn_index + 1) % max(len(self.captains), 1)


class DraftView(discord.ui.View):
    def __init__(self, state: DraftState, state_path: str):
        super().__init__(timeout=None)
        self.state = state
        self.state_path = state_path
        self._build_buttons()

    def _build_buttons(self) -> None:
        self.clear_items()
        for player in self.state.remaining_players:
            button = discord.ui.Button(label=player, style=discord.ButtonStyle.primary)
            button.callback = self._make_pick_callback(player)
            self.add_item(button)

    def _make_pick_callback(self, player: str):
        async def callback(interaction: discord.Interaction):
            user_id = interaction.user.id
            if user_id not in self.state.captains:
                await interaction.response.send_message(
                    "Vain valitut kapteenit voivat valita pelaajia.",
                    ephemeral=True,
                )
                return
            current = self.state.current_captain()
            if current != user_id:
                await interaction.response.send_message(
                    "Ei sinun vuorosi valita.",
                    ephemeral=True,
                )
                return
            try:
                self.state.register_pick(user_id, player)
            except ValueError:
                await interaction.response.send_message(
                    "Tämä pelaaja on jo valittu.",
                    ephemeral=True,
                )
                return
            self._build_buttons()
            self.state.save(self.state_path)
            await interaction.response.edit_message(
                content=build_status_message(self.state),
                view=self if self.state.remaining_players else None,
            )

        return callback


def build_status_message(state: DraftState) -> str:
    if not state.remaining_players:
        return "Drafti valmis!"
    captain_id = state.current_captain()
    if captain_id:
        return f"Seuraava vuoro: <@{captain_id}>"
    return "Seuraava vuoro"


def parse_player_list(raw: str) -> List[str]:
    players = [p.strip() for p in raw.split(",") if p.strip()]
    return players


intents = discord.Intents.default()
intents.message_content = True
bot = commands.Bot(command_prefix=COMMAND_PREFIX, intents=intents)
state = DraftState.load(STATE_PATH)


@bot.command(name="draft_setup")
async def draft_setup(ctx: commands.Context) -> None:
    mentions = ctx.message.mentions
    if len(mentions) < 2:
        await ctx.send("Lisää vähintään kaksi kapteenia komennossa.")
        return
    raw = ctx.message.content
    for mention in mentions:
        raw = raw.replace(mention.mention, "")
    raw = raw.replace(f"{ctx.prefix}{ctx.invoked_with}", "", 1).strip()
    players = parse_player_list(raw)
    if not players:
        await ctx.send("Lisää pelaajalista pilkuilla erotettuna.")
        return
    if len(players) > 25:
        await ctx.send("Pelaajia voi olla enintään 25 kerrallaan, jotta napit mahtuvat viestiin.")
        return
    state.captains = [m.id for m in mentions[:2]]
    state.remaining_players = players
    state.team_picks = {str(m.id): [] for m in mentions[:2]}
    state.turn_index = 0
    state.channel_id = None
    state.message_id = None
    state.save(STATE_PATH)
    await ctx.send("Draft asetettu. Käynnistä draft komennolla !draft_begin.")


@bot.command(name="draft_begin")
async def draft_begin(ctx: commands.Context) -> None:
    if not state.captains or not state.remaining_players:
        await ctx.send("Draft ei ole valmis. Käytä !draft_setup ensin.")
        return
    view = DraftView(state, STATE_PATH)
    msg = await ctx.send(build_status_message(state), view=view)
    state.channel_id = msg.channel.id
    state.message_id = msg.id
    state.save(STATE_PATH)


@bot.command(name="draft_status")
async def draft_status(ctx: commands.Context) -> None:
    await ctx.send(build_status_message(state))


if __name__ == "__main__":
    if not TOKEN:
        raise RuntimeError("DISCORD_TOKEN environment variable is required")
    bot.run(TOKEN)
