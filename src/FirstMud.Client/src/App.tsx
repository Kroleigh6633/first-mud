import GameTerminal from './components/GameTerminal'
import { useGameConnection } from './hooks/useGameConnection'

function App() {
  const game = useGameConnection()
  return <GameTerminal {...game} />
}

export default App
