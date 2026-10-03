const uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
const lowercase = "abcdefghijkmnopqrstuvwxyz";
const digits = "23456789";
const symbols = "!@#$%^&*-_=+?";
const alphabet = uppercase + lowercase + digits + symbols;

function randomIndex(length: number): number {
  const random = new Uint8Array(1);
  const limit = 256 - (256 % length);
  do { crypto.getRandomValues(random); } while (random[0] >= limit);
  return random[0] % length;
}

export function generateStrongPassword(): string {
  const characters = [uppercase, lowercase, digits, symbols].map((group) => group[randomIndex(group.length)]);
  while (characters.length < 24) characters.push(alphabet[randomIndex(alphabet.length)]);
  for (let index = characters.length - 1; index > 0; index--) {
    const next = randomIndex(index + 1);
    [characters[index], characters[next]] = [characters[next], characters[index]];
  }
  return characters.join("");
}
