CS2 1v1 Arena Plugin

This plugin adds a 1v1 arena duel system to Counter-Strike 2 servers. Players can challenge each other using a command-based invitation system and fight with a selected weapon in a controlled arena environment.

🔫 Features

Challenge another player using:

<pre>
!lr <player name> <weapon>
!arena <player name> <weapon>
</pre>

The challenged player must accept the duel by typing:

<pre>
!kabul
!accept
</pre>

<details>
<pre>
-Duels are fought using the selected weapon.
-Only one arena match can run at a time.
-While a match is active, no other players can enter the arena.
-Each player has 3 duel rights.
-After using all 3 rights, the player receives a 5-minute cooldown.
-Weapon restore system:
-Example: If you enter the arena with an AK-47 and win using an M4, once the duel ends you will be returned to the weapon you had before entering the arena.
-Configurable options:
-Enable or disable duels against teammates.
-Customize plugin behavior based on your server’s game modes and content.
-Arena countdown system:
-If the timer ends and no player wins, the duel results in a draw.
-Both players are declared tied and sent back down.
-Fully configurable via config files for server owners.
</details>
</pre>

⚙️ Customization
All major features can be adjusted through configuration files, allowing server owners to tailor the arena system to their server’s needs and playstyle.

Video; 

https://www.youtube.com/watch?v=I-vULOsPl1g

Model;

https://steamcommunity.com/sharedfiles/filedetails/?id=3655978004
